namespace Nop.Plugin.Misc.MultiTenantStores.Controllers.Public
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Core.Domain.Orders;
    using Nop.Core.Domain.Payments;
    using Nop.Services.Orders;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// پرداخت اعتباری/اقساطی (BNPL) — دیجی‌پی و اسنپ‌پی.
    /// جایگزین BnplController قبلی (که فقط اسکلت با Endpoint نمادین بود) با یکپارچه‌سازی واقعی.
    /// </summary>
    [ApiController]
    [Route("api/checkout/bnpl")]
    public class BnplController : ControllerBase
    {
        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly IBnplService _bnplService;
        private readonly IOrderService _orderService;
        private readonly IOrderProcessingService _orderProcessingService;

        public BnplController(
            IWorkContext workContext,
            IStoreContext storeContext,
            IBnplService bnplService,
            IOrderService orderService,
            IOrderProcessingService orderProcessingService)
        {
            _workContext = workContext;
            _storeContext = storeContext;
            _bnplService = bnplService;
            _orderService = orderService;
            _orderProcessingService = orderProcessingService;
        }

        /// <summary>بررسی اجازه/صلاحیت خرید اعتباری (قبل از نمایش دکمه).</summary>
        [HttpPost("check-eligibility")]
        public async Task<IActionResult> CheckEligibility([FromBody] BnplEligibilityRequestDto dto)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();

            if (dto == null || string.IsNullOrWhiteSpace(dto.ProviderKey))
                return BadRequest(new { success = false, message = "ارائه‌دهندهٔ BNPL مشخص نشده است." });

            var result = await _bnplService.CheckEligibilityAsync(
                store.Id, customer.Id, dto.ProviderKey.Trim().ToLowerInvariant(), dto.AmountToman, dto.CustomerMobile, dto.CustomerNationalId);

            return Ok(new { success = true, isEligible = result.IsEligible, providerKey = result.ProviderKey, message = result.Message });
        }

        /// <summary>شروع پرداخت اعتباری: ایجاد تیکت/توکن و برگرداندن آدرس هدایت به درگاه.</summary>
        [HttpPost("start-payment")]
        public async Task<IActionResult> StartPayment([FromBody] BnplStartPaymentRequestDto dto)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();

            if (dto == null || dto.OrderId <= 0 || string.IsNullOrWhiteSpace(dto.ProviderKey))
                return BadRequest(new { success = false, message = "اطلاعات پرداخت اعتباری ناقص است." });

            var order = await _orderService.GetOrderByIdAsync(dto.OrderId);
            if (order == null || order.CustomerId != customer.Id)
                return NotFound(new { success = false, message = "سفارش یافت نشد." });

            var result = await _bnplService.StartPaymentAsync(
                store.Id, order.Id, customer.Id, dto.ProviderKey.Trim().ToLowerInvariant(),
                order.OrderTotal, dto.CallbackUrl, dto.CustomerMobile ?? customer.Phone);

            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = result.Message, transactionId = result.TransactionId });

            return Ok(new
            {
                success = true,
                redirectUrl = result.RedirectUrl,
                transactionId = result.TransactionId,
                paymentToken = result.PaymentToken,
                providerKey = dto.ProviderKey
            });
        }

        /// <summary>تایید نهایی پرداخت (بازگشت از درگاه) — فقط بعد از تایید واقعی ارائه‌دهنده.</summary>
        [HttpPost("verify-payment")]
        public async Task<IActionResult> VerifyPayment([FromBody] BnplVerifyRequestDto dto)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();

            if (dto == null || string.IsNullOrWhiteSpace(dto.ProviderKey) || string.IsNullOrWhiteSpace(dto.TransactionId))
                return BadRequest(new { success = false, message = "اطلاعات تایید ناقص است." });

            var verifyResult = await _bnplService.VerifyPaymentAsync(
                store.Id, dto.ProviderKey.Trim().ToLowerInvariant(), dto.TransactionId, dto.PaymentToken, dto.AmountToman);

            if (!verifyResult.IsSuccess)
                return BadRequest(new { success = false, message = verifyResult.Message });

            // در صورت موفقیت، سفارش را پرداخت‌شده علامت بزن (فقط اگر قبلاً پرداخت نشده باشد)
            var order = await _orderService.GetOrderByIdAsync(dto.OrderId);
            if (order != null && order.PaymentStatusId != (int)PaymentStatus.Paid)
            {
                order.PaymentMethodSystemName = $"BNPL:{dto.ProviderKey}";
                await _orderService.UpdateOrderAsync(order);
                await _orderProcessingService.MarkOrderAsPaidAsync(order);
            }

            return Ok(new
            {
                success = true,
                alreadyProcessed = verifyResult.AlreadyProcessed,
                trackingCode = verifyResult.TrackingCode,
                message = verifyResult.Message
            });
        }
    }

    public class BnplEligibilityRequestDto
    {
        public string ProviderKey { get; set; }
        public decimal AmountToman { get; set; }
        public string CustomerMobile { get; set; }
        public string CustomerNationalId { get; set; }
    }

    public class BnplStartPaymentRequestDto
    {
        public int OrderId { get; set; }
        public string ProviderKey { get; set; }
        public string CallbackUrl { get; set; }
        public string CustomerMobile { get; set; }
    }

    public class BnplVerifyRequestDto
    {
        public int OrderId { get; set; }
        public string ProviderKey { get; set; }
        public string TransactionId { get; set; }
        public string PaymentToken { get; set; }
        public decimal AmountToman { get; set; }
    }
}
