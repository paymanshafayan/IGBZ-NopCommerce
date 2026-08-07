namespace Nop.Plugin.Misc.MultiTenantStores.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using Nop.Core;
    using Nop.Services.Catalog;
    using Nop.Services.Media;
    using Nop.Services.Seo;

    /// <summary>
    /// Omnichannel Marketplace Sync Service for Torob, Digikala & Kenar Divar (.NET 9 / nopCommerce 4.90)
    /// </summary>
    public interface IMarketplaceOmnichannelService
    {
        Task<TorobProductFeedResult> GetTorobLiveJsonFeedAsync(int storeId, int page = 1, int pageSize = 100);
        Task<bool> SyncStockAndPriceWithDigikalaAsync(string digikalaSellerToken, string sellerVariantId, int newStockCount, decimal newPriceToman);
        Task<DivarPostResult> PublishPostOnKenarDivarAsync(string divarAccessToken, string title, string description, decimal priceToman, string imageUrl);
    }

    public class MarketplaceOmnichannelService : IMarketplaceOmnichannelService
    {
        private readonly IProductService _productService;
        private readonly IPictureService _pictureService;
        private readonly IUrlRecordService _urlRecordService;
        private readonly IWebHelper _webHelper;
        private readonly IHttpClientFactory _httpClientFactory;

        public MarketplaceOmnichannelService(
            IProductService productService,
            IPictureService pictureService,
            IUrlRecordService urlRecordService,
            IWebHelper webHelper,
            IHttpClientFactory httpClientFactory)
        {
            _productService = productService;
            _pictureService = pictureService;
            _urlRecordService = urlRecordService;
            _webHelper = webHelper;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// خروجی JSON لایو برای موتور ربات‌های ترب (Torob API)، ساخته‌شده از کاتالوگ واقعی فروشگاه
        /// (نه چند آیتم نمونه ثابت).
        /// </summary>
        public async Task<TorobProductFeedResult> GetTorobLiveJsonFeedAsync(int storeId, int page = 1, int pageSize = 100)
        {
            var products = await _productService.SearchProductsAsync(
                pageIndex: page - 1,
                pageSize: pageSize,
                storeId: storeId,
                visibleIndividuallyOnly: true);

            var storeBaseUrl = _webHelper.GetStoreLocation();
            var items = new List<TorobProductItemDto>();

            foreach (var product in products)
            {
                var seName = await _urlRecordService.GetSeNameAsync(product);
                var pictures = await _pictureService.GetPicturesByProductIdAsync(product.Id, 1);
                var picture = pictures.FirstOrDefault();
                var (imageUrl, _) = picture != null
                    ? await _pictureService.GetPictureUrlAsync(picture)
                    : (string.Empty, string.Empty);

                items.Add(new TorobProductItemDto
                {
                    PageUniqueId = $"prod-{product.Id}",
                    Title = product.Name,
                    PriceToman = product.Price,
                    OldPriceToman = product.OldPrice,
                    IsAvailability = product.Published && (product.StockQuantity > 0 || !product.ManageInventoryMethodId.Equals(1)),
                    ProductUrl = $"{storeBaseUrl.TrimEnd('/')}/{seName}",
                    ImageUrl = imageUrl,
                    CategoryName = string.Empty
                });
            }

            return new TorobProductFeedResult
            {
                TotalCount = products.TotalCount,
                Page = page,
                PageSize = pageSize,
                Products = items
            };
        }

        /// <summary>
        /// همگام‌سازی زنده قیمت/موجودی با Digikala Open API — نتیجه واقعی بر اساس پاسخ HTTP دیجی‌کالا
        /// برگردانده می‌شود، نه مقدار ثابت true.
        /// </summary>
        public async Task<bool> SyncStockAndPriceWithDigikalaAsync(string digikalaSellerToken, string sellerVariantId, int newStockCount, decimal newPriceToman)
        {
            if (string.IsNullOrWhiteSpace(digikalaSellerToken) || string.IsNullOrWhiteSpace(sellerVariantId))
                return false;

            var httpClient = _httpClientFactory.CreateClient("DigikalaOpenApi");
            httpClient.DefaultRequestHeaders.Remove("Authorization");
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {digikalaSellerToken}");

            try
            {
                var response = await httpClient.PatchAsync(
                    $"https://openapi.digikala.com/v1/seller/variants/{sellerVariantId}/stock-price",
                    JsonContent.Create(new DigikalaStockPricePayload
                    {
                        StockCount = newStockCount,
                        PriceRials = newPriceToman * 10
                    }));

                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException)
            {
                return false;
            }
        }

        /// <summary>
        /// ثبت آگهی فروشگاهی در دیوار (Kenar Divar API) — نتیجه و لینک آگهی از پاسخ واقعی API خوانده می‌شود.
        /// </summary>
        public async Task<DivarPostResult> PublishPostOnKenarDivarAsync(string divarAccessToken, string title, string description, decimal priceToman, string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(divarAccessToken))
            {
                return new DivarPostResult { IsSuccess = false, Message = "توکن دسترسی کنار دیوار برای این فروشگاه تنظیم نشده است." };
            }

            var httpClient = _httpClientFactory.CreateClient("KenarDivarApi");
            httpClient.DefaultRequestHeaders.Remove("Authorization");
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {divarAccessToken}");

            try
            {
                var response = await httpClient.PostAsJsonAsync("https://api.divar.ir/v1/open-platform/finder/post", new DivarCreatePostPayload
                {
                    Title = title,
                    Description = description,
                    PriceRials = priceToman * 10,
                    ImageUrl = imageUrl
                });

                var rawBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    return new DivarPostResult { IsSuccess = false, Message = $"کنار دیوار درخواست را رد کرد (کد {(int)response.StatusCode}): {rawBody}" };
                }

                var payload = await response.Content.ReadFromJsonAsync<DivarCreatePostResponse>();
                return new DivarPostResult
                {
                    IsSuccess = true,
                    PostToken = payload?.PostToken,
                    PostUrl = payload?.PostUrl,
                    Message = "آگهی فروشگاهی با موفقیت در کنار دیوار منتشر شد."
                };
            }
            catch (HttpRequestException ex)
            {
                return new DivarPostResult { IsSuccess = false, Message = $"ارتباط با API دیوار برقرار نشد: {ex.Message}" };
            }
        }
    }

    public class TorobProductFeedResult
    {
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public List<TorobProductItemDto> Products { get; set; }
    }

    public class TorobProductItemDto
    {
        public string PageUniqueId { get; set; }
        public string Title { get; set; }
        public decimal PriceToman { get; set; }
        public decimal OldPriceToman { get; set; }
        public bool IsAvailability { get; set; }
        public string ProductUrl { get; set; }
        public string ImageUrl { get; set; }
        public string CategoryName { get; set; }
    }

    public class DivarPostResult
    {
        public bool IsSuccess { get; set; }
        public string PostToken { get; set; }
        public string PostUrl { get; set; }
        public string Message { get; set; }
    }

    internal class DigikalaStockPricePayload
    {
        [JsonPropertyName("stock_count")] public int StockCount { get; set; }
        [JsonPropertyName("price_rials")] public decimal PriceRials { get; set; }
    }

    internal class DivarCreatePostPayload
    {
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("description")] public string Description { get; set; }
        [JsonPropertyName("price_rials")] public decimal PriceRials { get; set; }
        [JsonPropertyName("image_url")] public string ImageUrl { get; set; }
    }

    internal class DivarCreatePostResponse
    {
        [JsonPropertyName("post_token")] public string PostToken { get; set; }
        [JsonPropertyName("post_url")] public string PostUrl { get; set; }
    }
}
