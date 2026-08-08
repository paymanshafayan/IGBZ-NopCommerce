namespace Nop.Plugin.Misc.InstagramAssistant.Consumers
{
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Nop.Core;
    using Nop.Core.Domain.Catalog;
    using Nop.Core.Events;
    using Nop.Services.Catalog;
    using Nop.Services.Events;
    using Nop.Services.Media;
    using Nop.Services.Seo;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// ایونت کانسیومر nopCommerce: شنود اضافه شدن محصول جدید در هر فروشگاه چندمستاجره و انتشار
    /// خودکار پست معرفی محصول در صفحهٔ اینستاگرام تننت (Instagram Graph API — Content Publishing).
    /// نسخهٔ قبلی کپشن را می‌ساخت اما هرگز جایی ارسال/ذخیره نمی‌کرد (Task.CompletedTask خالی).
    /// </summary>
    public class ProductInsertedInstagramConsumer : IConsumer<EntityInsertedEvent<Product>>
    {
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;
        private readonly IPictureService _pictureService;
        private readonly IUrlRecordService _urlRecordService;
        private readonly IStoreContext _storeContext;
        private readonly ITenantIntegrationCredentialService _credentialService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly Services.IProductPhotoAiStudioService _photoStudioService;

        public ProductInsertedInstagramConsumer(
            IProductService productService,
            ICategoryService categoryService,
            IPictureService pictureService,
            IUrlRecordService urlRecordService,
            IStoreContext storeContext,
            ITenantIntegrationCredentialService credentialService,
            IHttpClientFactory httpClientFactory,
            Services.IProductPhotoAiStudioService photoStudioService)
        {
            _productService = productService;
            _categoryService = categoryService;
            _pictureService = pictureService;
            _urlRecordService = urlRecordService;
            _storeContext = storeContext;
            _credentialService = credentialService;
            _httpClientFactory = httpClientFactory;
            _photoStudioService = photoStudioService;
        }

        public async Task HandleEventAsync(EntityInsertedEvent<Product> eventMessage)
        {
            var product = eventMessage.Entity;
            if (product == null || !product.Published || product.Deleted) return;

            var currentStore = await _storeContext.GetCurrentStoreAsync();
            var credentials = await _credentialService.GetByStoreIdAsync(currentStore.Id);
            var igCredential = credentials.FirstOrDefault(c => c.ProviderKey == "instagram.graph" && c.IsActive);

            // بدون اعتبارنامهٔ واقعی، هیچ پستی منتشر نمی‌شود — نه یک انتشار جعلی/خاموش.
            if (igCredential == null)
                return;

            var pictures = await _pictureService.GetPicturesByProductIdAsync(product.Id, 1);
            var picture = pictures.FirstOrDefault();
            if (picture == null)
                return; // Instagram Content Publishing API بدون تصویر امکان‌پذیر نیست

            // طبق نیازمندی صریح کاربر: کد محصول باید در گوشهٔ تصویرِ پست دیده شود، نه فقط در متن
            // کپشن. قبلاً این‌جا فقط URL خام عکس آپلودی مستقیماً پست می‌شد — بدون هیچ واترمارکی.
            var imageUrl = await BuildWatermarkedImageUrlOrFallbackAsync(picture, product.Sku);
            if (string.IsNullOrEmpty(imageUrl))
                return;

            var productCategories = await _categoryService.GetProductCategoriesByProductIdAsync(product.Id);
            var firstProductCategory = productCategories.FirstOrDefault();
            var categoryName = "عمومی";
            if (firstProductCategory != null)
            {
                var category = await _categoryService.GetCategoryByIdAsync(firstProductCategory.CategoryId);
                if (category != null)
                    categoryName = category.Name;
            }
            var caption = BuildInstagramCaption(product, categoryName);
            var accessToken = _credentialService.DecryptForActualUse(igCredential.ApiKey);

            await PublishToInstagramAsync(accessToken, imageUrl, caption);
        }

        /// <summary>
        /// عکس محصول را با کد SKU واترمارک می‌کند و به‌عنوان یک Picture جدید ذخیره می‌کند تا URL
        /// عمومی مخصوص به خودش را داشته باشد (بدون دست‌کاری فایل اصلی محصول در فروشگاه). اگر SKU
        /// خالی باشد یا واترمارک با خطا مواجه شود، به تصویر خام اصلی برمی‌گردد (نه این‌که پست را
        /// کلاً لغو کند) — بهتر است پست بدون واترمارک منتشر شود تا اصلاً منتشر نشود.
        /// </summary>
        private async Task<string> BuildWatermarkedImageUrlOrFallbackAsync(Nop.Core.Domain.Media.Picture picture, string productSku)
        {
            var (originalUrl, _) = await _pictureService.GetPictureUrlAsync(picture);

            if (string.IsNullOrWhiteSpace(productSku))
                return originalUrl;

            try
            {
                var rawBytes = await _pictureService.LoadPictureBinaryAsync(picture);
                if (rawBytes == null || rawBytes.Length == 0)
                    return originalUrl;

                var watermarkedBytes = await _photoStudioService.ApplyDynamicSkuWatermarkAsync(rawBytes, productSku);

                var newPicture = await _pictureService.InsertPictureAsync(
                    watermarkedBytes, "image/jpeg", $"instagram-post-{productSku}");

                var (watermarkedUrl, _) = await _pictureService.GetPictureUrlAsync(newPicture);
                return string.IsNullOrEmpty(watermarkedUrl) ? originalUrl : watermarkedUrl;
            }
            catch (System.Exception)
            {
                // واترمارک نافرجام نباید کل انتشار پست را متوقف کند.
                return originalUrl;
            }
        }

        /// <summary>
        /// انتشار واقعی پست از طریق دو مرحلهٔ استاندارد Instagram Graph Content Publishing API:
        /// ۱) ساخت Media Container  ۲) انتشار Container. کلید API (Page/IG Access Token) باید از
        /// قبل در پنل «اتصالات» (IntegrationCredentials) با ProviderKey = instagram.graph ثبت شده باشد.
        /// </summary>
        private async Task PublishToInstagramAsync(string accessToken, string imageUrl, string caption)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return;

            var httpClient = _httpClientFactory.CreateClient("InstagramGraphApi");

            var containerResponse = await httpClient.PostAsync(
                $"https://graph.facebook.com/v19.0/me/media?image_url={System.Uri.EscapeDataString(imageUrl)}" +
                $"&caption={System.Uri.EscapeDataString(caption)}&access_token={System.Uri.EscapeDataString(accessToken)}",
                null);

            if (!containerResponse.IsSuccessStatusCode)
                return; // شکست واقعی — بدون تلاش دوباره در همین Request؛ Retry باید در صف پس‌زمینه انجام شود (بخش ۱۰.۳ سند معماری)

            var containerPayload = await containerResponse.Content.ReadFromJsonAsync<InstagramContainerResponse>();
            if (string.IsNullOrEmpty(containerPayload?.Id))
                return;

            await httpClient.PostAsync(
                $"https://graph.facebook.com/v19.0/me/media_publish?creation_id={containerPayload.Id}" +
                $"&access_token={System.Uri.EscapeDataString(accessToken)}",
                null);
        }

        private string BuildInstagramCaption(Product product, string categoryName)
        {
            var formattedPrice = $"{product.Price:N0} تومان";
            var productSku = !string.IsNullOrEmpty(product.Sku) ? product.Sku : $"PROD-{product.Id}";
            var caption = $"🌟 رونمایی از محصول جدید: {product.Name}\r\n\r\n" +
                          $"▫️ کد محصول: {productSku}\r\n" +
                          $"▫️ دسته‌بندی: {categoryName}\r\n" +
                          $"▫️ قیمت ویژه: {formattedPrice}\r\n\r\n" +
                          $"------------------------------\r\n" +
                          $"💬 چطور این محصول را بخریم؟\r\n" +
                          $"کد محصول «{productSku}» را زیر همین پست کامنت کنید تا لینک مستقیم سفارش همراه با تخفیف اختصاصی بلافاصله به دایرکت شما ارسال شود! ⚡️\r\n\r\n" +
                          $"{GenerateSmartHashtags(product.Name, categoryName)}";

            return caption;
        }

        private string GenerateSmartHashtags(string productName, string categoryName)
        {
            var cleanCategory = categoryName.Replace(" ", "_");
            var cleanName = Regex.Replace(productName, @"[^\w\d_]", "").Replace(" ", "_");

            return $"#{cleanCategory} #{cleanName} #خرید_آنلاین #فروشگاه_اینترنتی #ارسال_سریع #خرید_دایرکتی #تخفیف_ویژه";
        }
    }

    internal class InstagramContainerResponse
    {
        public string Id { get; set; }
    }
}
