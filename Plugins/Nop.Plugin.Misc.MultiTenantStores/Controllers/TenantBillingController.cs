namespace Nop.Plugin.Misc.MultiTenantStores.Controllers
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Core.Domain.Orders;
    using Nop.Services.Orders;
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
        private readonly IOrderService _orderService;
        private readonly IOrderProcessingService _orderProcessingService;

        public TenantBillingController(
            IWorkContext workContext,
            ITenantPlanService tenantPlanService,
            IParbadPaymentService paymentService,
            IOrderService orderService,
            IOrderProcessingService orderProcessingService)
        {
            _workContext = workContext;
            _tenantPlanService = tenantPlanService;
            _paymentService = paymentService;
            _orderService = orderService;
            _orderProcessingService = orderProcessingService;
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

        /// <summary>
        /// تایید واقعی پرداخت سفارش تمدید/ارتقا (بازگشت از درگاه) — مانند مسیر ثبت‌نام، فقط بعد از
        /// تایید واقعی Parbad (نه صرفاً بازگشت کاربر) سفارش پرداخت‌شده علامت می‌خورد و اشتراک
        /// فعال می‌شود. CallbackUrl که اپ/سایت مادر به درگاه داده، باید به همین Endpoint اشاره کند.
        /// </summary>
        [HttpPost("verify-payment")]
        public async Task<IActionResult> VerifyRenewalPayment(
            [FromHeader(Name = "X-Store-Id")] int storeId,
            [FromBody] RenewalPaymentVerifyDto dto)
        {
            if (storeId <= 0 || dto == null || dto.OrderId <= 0 || string.IsNullOrWhiteSpace(dto.TrackingNumber) || dto.AmountToman <= 0)
                return BadRequest(new { success = false, message = "اطلاعات تایید پرداخت ناقص است." });

            var order = await _orderService.GetOrderByIdAsync(dto.OrderId);
            if (order == null)
                return NotFound(new { success = false, message = "سفارش تمدید یافت نشد." });

            var verifyResult = await _paymentService.VerifyPaymentAsync(
                TenantPlanService.MasterPlatformStoreId, dto.TrackingNumber, dto.AmountToman);

            if (!verifyResult.IsSuccess)
                return BadRequest(new { success = false, message = verifyResult.Message });

            if (order.PaymentStatusId != (int)PaymentStatus.Paid)
                await _orderProcessingService.MarkOrderAsPaidAsync(order);

            var subscriptionActivated = await _tenantPlanService.ActivateSubscriptionAsync(storeId);

            return Ok(new
            {
                success = true,
                alreadyProcessed = verifyResult.AlreadyVerifiedBefore,
                subscriptionActivated,
                message = "پرداخت تایید شد و اشتراک فروشگاه فعال گردید."
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

    public class RenewalPaymentVerifyDto
    {
        public int OrderId { get; set; }
        public string TrackingNumber { get; set; }
        public decimal AmountToman { get; set; }
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
