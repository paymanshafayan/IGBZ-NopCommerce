namespace Nop.Plugin.Misc.MultiTenantStores.Controllers.Admin
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Web.Framework;
    using Nop.Web.Framework.Controllers;
    using Nop.Web.Framework.Mvc.Filters;
    using Nop.Services.Catalog;
    using Nop.Services.Localization;
    using Nop.Services.Security;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// دکمه‌های «سئوی خودکار» و «ترجمهٔ خودکار» در صفحهٔ ویرایش محصول — طبق سند
    /// `سئو_و_تبلیغات.txt` و `سرویس_دهنده_های_واسط_مترجم.txt`. نسخهٔ قبلی این دو سرویس محتوا
    /// تولید می‌کرد ولی هرگز در دیتابیس واقعی محصول ذخیره نمی‌شد؛ این کنترلر آن حلقه را می‌بندد.
    /// </summary>
    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [AutoValidateAntiforgeryToken]
    [ServiceFilter(typeof(Infrastructure.Filters.TenantAdminScopeFilter))]
    public class ProductAiToolsController : BasePluginController
    {
        private readonly IProductService _productService;
        private readonly ILocalizedEntityService _localizedEntityService;
        private readonly IPermissionService _permissionService;
        private readonly ISeoAndAdNetworksFeedService _seoService;
        private readonly ICryptoAndTranslationService _translationService;
        private readonly ITenantIntegrationCredentialService _credentialService;
        private readonly Nop.Core.IStoreContext _storeContext;
        private readonly Nop.Services.Common.IGenericAttributeService _genericAttributeService;

        public ProductAiToolsController(
            IProductService productService,
            ILocalizedEntityService localizedEntityService,
            IPermissionService permissionService,
            ISeoAndAdNetworksFeedService seoService,
            ICryptoAndTranslationService translationService,
            ITenantIntegrationCredentialService credentialService,
            Nop.Core.IStoreContext storeContext,
            Nop.Services.Common.IGenericAttributeService genericAttributeService)
        {
            _productService = productService;
            _localizedEntityService = localizedEntityService;
            _permissionService = permissionService;
            _seoService = seoService;
            _translationService = translationService;
            _credentialService = credentialService;
            _storeContext = storeContext;
            _genericAttributeService = genericAttributeService;
        }

        /// <summary>
        /// تولید خودکار Meta Title/Description و **ذخیرهٔ واقعی** آن روی محصول
        /// (نسخهٔ قبلی این کار را انجام نمی‌داد — فقط یک DTO برمی‌گرداند که هیچ‌جا Save نمی‌شد).
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> GenerateSeo(int productId)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
                return Json(new { success = false, message = "دسترسی رد شد." });

            var product = await _productService.GetProductByIdAsync(productId);
            if (product == null)
                return Json(new { success = false, message = "محصول یافت نشد." });

            var seoResult = await _seoService.GenerateProductSeoMetaAsync(product.Name, product.FullDescription ?? product.ShortDescription);

            product.MetaTitle = seoResult.MetaTitle;
            product.MetaDescription = seoResult.MetaDescription;
            product.MetaKeywords = seoResult.Hashtags;
            await _productService.UpdateProductAsync(product);

            return Json(new
            {
                success = true,
                message = "متادیتای سئو تولید و روی محصول ذخیره شد.",
                metaTitle = seoResult.MetaTitle,
                metaDescription = seoResult.MetaDescription,
                metaKeywords = seoResult.Hashtags
            });
        }

        /// <summary>
        /// ترجمهٔ خودکار نام/توضیحات محصول و **ذخیرهٔ واقعی** آن در جدول LocalizedProperty
        /// ناپ‌کامرس از طریق ILocalizedEntityService (نسخهٔ قبلی متن ترجمه‌شده را فقط برمی‌گرداند
        /// بدون اینکه هرگز در دیتابیس ثبت شود — یعنی نسخهٔ زبان دیگر سایت هرگز به‌روز نمی‌شد).
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> TranslateProduct(int productId, int targetLanguageId, string targetLanguageCode)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
                return Json(new { success = false, message = "دسترسی رد شد." });

            var product = await _productService.GetProductByIdAsync(productId);
            if (product == null)
                return Json(new { success = false, message = "محصول یافت نشد." });

            var currentStore = await _storeContext.GetCurrentStoreAsync();
            var credentials = await _credentialService.GetByStoreIdAsync(currentStore.Id);
            var translationCredential = credentials.FirstOrDefaultProviderKey("tarjomyar")
                ?? credentials.FirstOrDefaultProviderKey("farazin");

            if (translationCredential == null)
                return Json(new { success = false, message = "هیچ کلید API سرویس ترجمه (ترجمیار/فرازین) برای این فروشگاه فعال نیست." });

            var apiKey = _credentialService.DecryptForActualUse(translationCredential.ApiKey);
            var result = await _translationService.AutoTranslateProductCatalogAsync(
                apiKey, product.Name, product.FullDescription ?? product.ShortDescription, targetLanguageCode);

            if (!result.IsSuccess)
                return Json(new { success = false, message = result.Message });

            // ذخیرهٔ واقعی در جدول LocalizedProperty ناپ‌کامرس — این خط دقیقاً همان چیزی است که در
            // نسخهٔ قبلی جا افتاده بود.
            await _localizedEntityService.SaveLocalizedValueAsync(product, p => p.Name, result.TranslatedName, targetLanguageId);
            await _localizedEntityService.SaveLocalizedValueAsync(product, p => p.FullDescription, result.TranslatedDescription, targetLanguageId);

            return Json(new { success = true, message = "ترجمه با موفقیت انجام و در دیتابیس ذخیره شد." });
        }

        /// <summary>
        /// ذخیرهٔ شناسهٔ Variant دیجی‌کالا به‌عنوان GenericAttribute روی محصول — این مقدار همان چیزی
        /// است که MarketplaceSyncScheduleTask موقع همگام‌سازی موجودی/قیمت با دیجی‌کالا می‌خواند
        /// (قبلاً به‌اشتباه از product.Sku استفاده می‌شد که SKU داخلی فروشگاه را با شناسهٔ بیرونی
        /// دیجی‌کالا قاطی می‌کرد).
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> SaveDigikalaVariantId(int productId, string digikalaVariantId)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
                return Json(new { success = false, message = "دسترسی رد شد." });

            var product = await _productService.GetProductByIdAsync(productId);
            if (product == null)
                return Json(new { success = false, message = "محصول یافت نشد." });

            await _genericAttributeService.SaveAttributeAsync(
                product,
                Nop.Plugin.Misc.MultiTenantStores.Tasks.MarketplaceSyncScheduleTask.DigikalaVariantIdAttributeKey,
                digikalaVariantId?.Trim());

            return Json(new { success = true, message = "شناسهٔ Variant دیجی‌کالا ذخیره شد." });
        }
    }

    internal static class CredentialListExtensions
    {
        public static Domain.TenantIntegrationCredential FirstOrDefaultProviderKey(
            this System.Collections.Generic.IList<Domain.TenantIntegrationCredential> credentials, string providerKey)
        {
            foreach (var c in credentials)
                if (c.ProviderKey == providerKey && c.IsActive)
                    return c;
            return null;
        }
    }
}
