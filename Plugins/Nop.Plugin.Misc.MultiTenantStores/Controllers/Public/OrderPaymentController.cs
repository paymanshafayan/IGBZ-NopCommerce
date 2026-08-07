namespace Nop.Plugin.Misc.MultiTenantStores.Controllers.Public
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Core.Domain.Orders;
    using Nop.Services.Orders;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// پرداخت سفارش با دو گزینهٔ واقعی: کیف‌پول واحد پلتفرم یا درگاه پرداخت مستقیم. قبلاً این
    /// کنترلر در پلاگین InstagramAssistant بود (چون کیف‌پول آن‌جا تعریف شده بود)؛ حالا که کیف‌پول
    /// یکپارچه به هستهٔ پلتفرم منتقل شده، این کنترلر هم به‌همراهش منتقل شد.
    /// </summary>
    [ApiController]
    [Route("api/orders/{orderId}/payment")]
    public class OrderPaymentController : ControllerBase
    {
        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly IOrderService _orderService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IWalletService _walletService;
        private readonly IParbadPaymentService _paymentService;

        public OrderPaymentController(
            IWorkContext workContext,
            IStoreContext storeContext,
            IOrderService orderService,
            IOrderProcessingService orderProcessingService,
            IWalletService walletService,
            IParbadPaymentService paymentService)
        {
            _workContext = workContext;
            _storeContext = storeContext;
            _orderService = orderService;
            _orderProcessingService = orderProcessingService;
            _walletService = walletService;
            _paymentService = paymentService;
        }

        /// <summary>اپ فلاتر بر اساس این پاسخ، دو دکمه («پرداخت از کیف‌پول» / «پرداخت مستقیم») می‌سازد؛ اگر موجودی کافی نباشد، دکمهٔ کیف‌پول غیرفعال می‌شود.</summary>
        [HttpGet("options")]
        public async Task<IActionResult> GetPaymentOptions(int orderId)
        {
            var (order, errorResult) = await ResolveOwnedOrderAsync(orderId);
            if (errorResult != null) return errorResult;

            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();
            var walletBalance = await _walletService.GetBalanceAsync(customer.Id, store.Id);

            return Ok(new
            {
                orderId = order.Id,
                amountToman = order.OrderTotal,
                alreadyPaid = order.PaymentStatusId == (int)PaymentStatus.Paid,
                wallet = new
                {
                    // طبق درخواست: اگر موجودی کافی نباشد، گزینهٔ کیف‌پول غیرفعال است.
                    available = walletBalance >= order.OrderTotal,
                    currentBalanceToman = walletBalance
                },
                gateway = new { available = true }
            });
        }

        [HttpPost("pay-with-wallet")]
        public async Task<IActionResult> PayWithWallet(int orderId)
        {
            var (order, errorResult) = await ResolveOwnedOrderAsync(orderId);
            if (errorResult != null) return errorResult;

            if (order.PaymentStatusId == (int)PaymentStatus.Paid)
                return Ok(new { success = true, alreadyPaid = true, message = "این سفارش قبلاً پرداخت شده است." });

            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();

            var (debitSuccess, newBalance, errorMessage) = await _walletService.TryDebitAsync(
                customer.Id, store.Id, order.OrderTotal, WalletTransactionReason.OrderPaymentDebit, referenceCode: $"order-wallet-{order.Id}");

            if (!debitSuccess)
                return BadRequest(new { success = false, message = errorMessage, currentBalanceToman = newBalance });

            // یادداشت: "Wallet" یک PaymentMethodSystemName واقعی ثبت‌شدهٔ nopCommerce نیست (چون هیچ
            // IPaymentMethod برای آن ساخته نشده)؛ صرفاً برای شناسایی روش پرداخت در گزارش‌های داخلی است.
            order.PaymentMethodSystemName = "Wallet";
            await _orderService.UpdateOrderAsync(order);
            await _orderProcessingService.MarkOrderAsPaidAsync(order);

            return Ok(new { success = true, newWalletBalanceToman = newBalance });
        }

        [HttpPost("pay-with-gateway")]
        public async Task<IActionResult> PayWithGateway(int orderId, [FromBody] GatewayPaymentRequestDto dto)
        {
            var (order, errorResult) = await ResolveOwnedOrderAsync(orderId);
            if (errorResult != null) return errorResult;

            if (order.PaymentStatusId == (int)PaymentStatus.Paid)
                return Ok(new { success = true, alreadyPaid = true, message = "این سفارش قبلاً پرداخت شده است." });

            var store = await _storeContext.GetCurrentStoreAsync();
            var result = await _paymentService.RequestPaymentAsync(
                store.Id, order.Id, order.OrderTotal, dto?.GatewayName ?? "zarinpal", dto?.CallbackUrl);

            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = result.Message });

            return Ok(new { success = true, redirectUrl = result.RedirectUrl, trackingNumber = result.TrackingNumber });
        }

        /// <summary>
        /// Callback بانک پس از پرداخت درگاه — دقیقاً مثل الگوی WalletController، سفارش فقط بعد از
        /// تایید واقعی VerifyPaymentAsync (نه صرفاً بازگشت کاربر) پرداخت‌شده علامت می‌خورد.
        /// </summary>
        [HttpPost("gateway-callback")]
        public async Task<IActionResult> GatewayCallback(int orderId, [FromBody] GatewayVerifyRequestDto dto)
        {
            var order = await _orderService.GetOrderByIdAsync(orderId);
            if (order == null)
                return NotFound(new { success = false, message = "سفارش یافت نشد." });

            var store = await _storeContext.GetCurrentStoreAsync();
            var verifyResult = await _paymentService.VerifyPaymentAsync(store.Id, dto.TrackingNumber, order.OrderTotal);
            if (!verifyResult.IsSuccess)
                return BadRequest(new { success = false, message = verifyResult.Message });

            if (verifyResult.AlreadyVerifiedBefore)
                return Ok(new { success = true, alreadyProcessed = true, message = "این تراکنش قبلاً پردازش شده است." });

            order.PaymentMethodSystemName = $"Gateway:{dto.GatewayName}";
            await _orderService.UpdateOrderAsync(order);
            await _orderProcessingService.MarkOrderAsPaidAsync(order);

            return Ok(new { success = true });
        }

        private async Task<(Order order, IActionResult errorResult)> ResolveOwnedOrderAsync(int orderId)
        {
            var order = await _orderService.GetOrderByIdAsync(orderId);
            if (order == null)
                return (null, NotFound(new { success = false, message = "سفارش یافت نشد." }));

            var customer = await _workContext.GetCurrentCustomerAsync();
            if (order.CustomerId != customer.Id)
                return (null, StatusCode(403, new { success = false, message = "این سفارش متعلق به شما نیست." }));

            return (order, null);
        }
    }

    public class GatewayPaymentRequestDto
    {
        public string GatewayName { get; set; }
        public string CallbackUrl { get; set; }
    }

    public class GatewayVerifyRequestDto
    {
        public string TrackingNumber { get; set; }
        public string GatewayName { get; set; }
    }
}
