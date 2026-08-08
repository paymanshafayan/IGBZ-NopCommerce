namespace Nop.Plugin.Misc.MultiTenantStores.Controllers.Public
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// «امکان پرو لباس توسط مشتری با هوش مصنوعی» (نیازمندی #۷) — مشتری عکس خودش و عکس محصول
    /// (لباس) را می‌فرستد و تصویرِ پوشیده‌شده با مدل IDM-VTON (محلی/ابری هیبریدی) دریافت می‌کند.
    /// هزینه از کیف‌پول واحد مشتری کسر می‌شود (الگوی مشترک AI Studio) و در صورت شکست، بازگردانده می‌شود.
    /// </summary>
    [ApiController]
    [Route("api/virtual-tryon")]
    public class VirtualTryOnController : ControllerBase
    {
        // هزینهٔ هر درخواست به تومان — در نسخهٔ کامل باید قابل‌ویرایش هر تننت باشد
        private const decimal TryOnTomanCost = 20000m;

        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly IWalletService _walletService;
        private readonly IVirtualTryOnService _tryOnService;
        private readonly ITenantPlanService _tenantPlanService;

        public VirtualTryOnController(
            IWorkContext workContext,
            IStoreContext storeContext,
            IWalletService walletService,
            IVirtualTryOnService tryOnService,
            ITenantPlanService tenantPlanService)
        {
            _workContext = workContext;
            _storeContext = storeContext;
            _walletService = walletService;
            _tryOnService = tryOnService;
            _tenantPlanService = tenantPlanService;
        }

        /// <summary>
        /// اجرای پرو لباس: عکس مشتری + عکس لباس → تصویر پوشیده‌شده.
        /// آدرس‌های تصویر باید قبلاً در یک فضای ذخیره‌سازی عمومی (مثل Picture ناپ‌کامرس یا CDN) باشند.
        ///
        /// ⚠️ نکات مهم برای گرفتن بهترین کیفیت (برای نمایش به کاربر در اپ/فرانت):
        /// ۱) پس‌زمینهٔ عکس کاربر: هرچه ساده‌تر و تک‌رنگ‌تر باشد، لبه‌های لباس دقیق‌تر پردازش می‌شوند.
        /// ۲) ژست کاربر: فرد نباید دست‌هایش را جلوی بدنش گره زده باشد؛ بهترین حالت، ایستادن صاف با
        ///    دست‌های کمی باز است.
        /// ۳) کیفیت عکس لباس: عکس لباس باید کاملاً واضح، بدون خطای دید و ترجیحاً به‌صورت تخت
        ///    (Flat-lay) گرفته شده باشد.
        /// </summary>
        [HttpPost("try-on")]
        public async Task<IActionResult> TryOn([FromBody] VirtualTryOnRequestDto dto)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            if (customer == null || customer.Id <= 0)
                return Unauthorized(new { success = false, message = "برای استفاده از پرو لباس، ابتدا وارد شوید." });

            var store = await _storeContext.GetCurrentStoreAsync();

            // فروشگاه باید پلن فعال داشته باشد (هر پلن پولی/آزمایشی)
            var activePlan = await _tenantPlanService.GetActivePlanForStoreAsync(store.Id);
            if (activePlan == null)
                return StatusCode(402, new { success = false, message = "فروشگاه پلن فعال ندارد. ابتدا اشتراک را فعال کنید." });

            if (dto == null || string.IsNullOrWhiteSpace(dto.PersonImageUrl) || string.IsNullOrWhiteSpace(dto.GarmentImageUrl))
                return BadRequest(new { success = false, message = "عکس مشتری و عکس لباس الزامی است." });

            var referenceCode = $"tryon-{Guid.NewGuid():N}";

            // کسر هزینه از کیف‌پول مشتری (Idempotent با ReferenceCode یکتا)
            var (debited, balanceAfterDebit, debitError) = await _walletService.TryDebitAsync(
                customer.Id, store.Id, TryOnTomanCost, WalletTransactionReason.AiFeatureUsageDebit, referenceCode);

            if (!debited)
                return StatusCode(402, new
                {
                    success = false,
                    message = debitError,
                    requiredToman = TryOnTomanCost,
                    currentBalanceToman = balanceAfterDebit
                });

            var result = await _tryOnService.TryOnAsync(
                store.Id,
                dto.PersonImageUrl,
                dto.GarmentImageUrl,
                dto.GarmentDescription,
                dto.Category ?? "upper_body");

            if (!result.IsSuccess)
            {
                // بازگشت خودکار هزینه در صورت شکست سرویس
                await _walletService.CreditAsync(
                    customer.Id, store.Id, TryOnTomanCost,
                    WalletTransactionReason.AiFeatureUsageRefund, $"{referenceCode}-refund");

                return BadRequest(new { success = false, message = result.Message });
            }

            return Ok(new
            {
                success = true,
                resultImageUrl = result.ResultImageUrl,
                provider = result.Provider,
                tomanCharged = TryOnTomanCost,
                newBalanceToman = await _walletService.GetBalanceAsync(customer.Id, store.Id)
            });
        }
    }

    public class VirtualTryOnRequestDto
    {
        /// <summary>
        /// URL عکس تمام‌قد مشتری.
        /// ⚠️ برای بهترین کیفیت: پس‌زمینهٔ ساده/تکرنگ + ایستادن صاف با دست‌های کمی باز (نه گره‌خورده جلوی بدن).
        /// </summary>
        public string PersonImageUrl { get; set; }

        /// <summary>
        /// URL عکس محصول (لباس).
        /// ⚠️ برای بهترین کیفیت: عکس کاملاً واضح، بدون خطای دید و ترجیحاً تخت (Flat-lay).
        /// </summary>
        public string GarmentImageUrl { get; set; }

        /// <summary>توضیح لباس (اختیاری — برای بهبود کیفیت مدل).</summary>
        public string GarmentDescription { get; set; }

        /// <summary>دستهٔ لباس: upper_body / lower_body / dresses (پیش‌فرض upper_body).</summary>
        public string Category { get; set; }
    }
}
