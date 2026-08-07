namespace Nop.Plugin.Misc.MultiTenantStores.Controllers
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Plugin.Misc.MultiTenantStores.Services;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;

    /// <summary>
    /// ⚠️ این کنترلر دو نقص واقعی داشت که در همین دور پیدا و رفع شد:
    /// ۱) GetSubscriptionStatus وقتی اشتراک واقعی وجود نداشت، داده‌ی کاملاً ساختگی برمی‌گرداند
    ///    (همیشه «Trial»، همیشه IsActive=true، همیشه ۱۴ روز — بدون هیچ ارتباطی با واقعیت).
    /// ۲) CreateRenewalOrder یک URL جعلی/Placeholder برمی‌گرداند («/pay/gateway-redirect؟...») و
    ///    هرگز واقعاً درگاه پرداخت را صدا نمی‌زد — IPaymentService و IOrderProcessingService تزریق
    ///    شده بودند ولی هیچ‌کدام هرگز استفاده نمی‌شدند.
    /// </summary>
    [ApiController]
    [Route("api/tenant/billing")]
    public class TenantBillingController : ControllerBase
    {
        private readonly IWorkContext _workContext;
        private readonly ITenantPlanService _tenantPlanService;
        private readonly IParbadPaymentService _paymentService;

        public TenantBillingController(
            IWorkContext workContext,
            ITenantPlanService tenantPlanService,
            IParbadPaymentService paymentService)
        {
            _workContext = workContext;
            _tenantPlanService = tenantPlanService;
            _paymentService = paymentService;
        }

        /// <summary>
        /// دریافت وضعیت واقعی اشتراک فروشگاه تننت — دیگر در صورت نبود اشتراک، داده‌ی ساختگی
        /// برنمی‌گرداند؛ صادقانه اعلام می‌کند که اشتراکی وجود ندارد.
        /// </summary>
        [HttpGet("subscription-status")]
        public async Task<IActionResult> GetSubscriptionStatus([FromHeader(Name = "X-Store-Id")] int storeId)
        {
            if (storeId <= 0) return BadRequest("شناسه فروشگاه معتبر نیست.");

            var currentSub = await _tenantPlanService.GetSubscriptionByStoreIdAsync(storeId);
            if (currentSub == null)
            {
                return Ok(new
                {
                    storeId,
                    hasSubscription = false,
                    message = "این فروشگاه هنوز هیچ اشتراکی ثبت نکرده است."
                });
            }

            var plan = await _tenantPlanService.GetPlanByIdAsync(currentSub.TenantPlanId);
            var isTrialActive = currentSub.Status == SubscriptionStatus.Trial
                && currentSub.TrialEndDateUtc.HasValue && currentSub.TrialEndDateUtc.Value > DateTime.UtcNow;
            var daysRemaining = (currentSub.NextBillingDateUtc - DateTime.UtcNow).Days;

            return Ok(new
            {
                storeId,
                hasSubscription = true,
                planId = currentSub.TenantPlanId,
                planName = plan?.Name,
                status = currentSub.Status.ToString(),
                isActive = currentSub.Status == SubscriptionStatus.Active || isTrialActive,
                daysRemaining = Math.Max(0, daysRemaining),
                expiryDateUtc = currentSub.NextBillingDateUtc,
                autoRenew = currentSub.AutoRenew
            });
        }

        /// <summary>
        /// صدور فاکتور ارتقا/تمدید اشتراک و لینک واقعی پرداخت (نه یک URL Placeholder).
        /// </summary>
        [HttpPost("create-renewal-order")]
        public async Task<IActionResult> CreateRenewalOrder(
            [FromHeader(Name = "X-Store-Id")] int storeId,
            [FromBody] RenewalRequestDto dto)
        {
            var plan = await _tenantPlanService.GetPlanByIdAsync(dto.PlanId);
            if (plan == null) return NotFound("پلن مورد نظر یافت نشد.");

            var currentCustomer = await _workContext.GetCurrentCustomerAsync();
            if (currentCustomer == null)
                return Unauthorized("برای ثبت سفارش تمدید، ورود به حساب کاربری الزامی است.");

            var billingCycle = ParseBillingCycle(dto.BillingCycle);
            var orderId = await _tenantPlanService.CreateSubscriptionOrderAsync(storeId, currentCustomer.Id, plan.Id, billingCycle);

            if (orderId == 0)
            {
                // پلن آزمایشی — نیازی به پرداخت نیست.
                return Ok(new { success = true, requiresPayment = false, message = "اشتراک آزمایشی رایگان فعال شد." });
            }

            var amount = billingCycle switch
            {
                BillingCycle.SixMonths => plan.PriceSixMonths,
                BillingCycle.Yearly => plan.PriceYearly,
                _ => plan.PriceMonthly
            };

            var paymentResult = await _paymentService.RequestPaymentAsync(
                TenantPlanService.MasterPlatformStoreId, orderId, amount, dto.GatewayName ?? "zarinpal", dto.CallbackUrl);

            if (!paymentResult.IsSuccess)
                return BadRequest(new { success = false, message = paymentResult.Message });

            return Ok(new
            {
                success = true,
                requiresPayment = true,
                orderId,
                amountToman = amount,
                paymentRedirectUrl = paymentResult.RedirectUrl,
                trackingNumber = paymentResult.TrackingNumber
            });
        }

        private static BillingCycle ParseBillingCycle(string value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "sixmonths" or "six_months" or "6months" => BillingCycle.SixMonths,
                "yearly" or "annual" => BillingCycle.Yearly,
                _ => BillingCycle.Monthly
            };
        }
    }

    public class RenewalRequestDto
    {
        public int PlanId { get; set; }

        /// <summary>"monthly" | "sixmonths" | "yearly"</summary>
        public string BillingCycle { get; set; }
        public string GatewayName { get; set; }
        public string CallbackUrl { get; set; }
    }
}
