namespace Nop.Plugin.Misc.MultiTenantStores.Controllers.Public
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// کیف‌پول واحد پلتفرم — جایگزین AiCreditWalletController قدیمی (که مخصوص اعتبار AI بود).
    /// این کیف‌پول برای همه‌چیز استفاده می‌شود: مصرف قابلیت‌های AI، پرداخت سفارش، کش‌بک، حمایت مالی
    /// اینستاگرام، کمیسیون Affiliate. شارژ نقدی از طریق همان درگاه پرداخت واقعی (Parbad) انجام می‌شود.
    /// </summary>
    [ApiController]
    [Route("api/wallet")]
    public class WalletController : ControllerBase
    {
        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly IWalletService _walletService;

        public WalletController(IWorkContext workContext, IStoreContext storeContext, IWalletService walletService)
        {
            _workContext = workContext;
            _storeContext = storeContext;
            _walletService = walletService;
        }

        [HttpGet("balance")]
        public async Task<IActionResult> GetBalance()
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();
            var balance = await _walletService.GetBalanceAsync(customer.Id, store.Id);

            return Ok(new { balanceToman = balance });
        }

        /// <summary>درخواست شارژ نقدی — کاربر مبلغ تومانی را انتخاب می‌کند، به درگاه بانک هدایت می‌شود.</summary>
        [HttpPost("cash-topup/request")]
        public async Task<IActionResult> RequestCashTopUp([FromBody] CashTopUpRequestDto dto)
        {
            var store = await _storeContext.GetCurrentStoreAsync();
            var result = await _walletService.RequestCashTopUpAsync(store.Id, dto.AmountToman, dto.GatewayName, dto.CallbackUrl);

            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = result.Message });

            return Ok(new { success = true, redirectUrl = result.RedirectUrl, trackingNumber = result.TrackingNumber });
        }

        /// <summary>Callback بانک — فقط بعد از تایید واقعی بانک، مبلغ به کیف‌پول واریز می‌شود.</summary>
        [HttpPost("cash-topup/verify")]
        public async Task<IActionResult> VerifyCashTopUp([FromBody] CashTopUpVerifyDto dto)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();

            var result = await _walletService.VerifyCashTopUpAsync(customer.Id, store.Id, dto.TrackingNumber, dto.AmountToman);
            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = result.Message });

            return Ok(new
            {
                success = true,
                alreadyProcessed = result.AlreadyProcessed,
                message = result.Message,
                newBalanceToman = result.NewBalanceToman
            });
        }
    }

    public class CashTopUpRequestDto
    {
        public decimal AmountToman { get; set; }
        public string GatewayName { get; set; }
        public string CallbackUrl { get; set; }
    }

    public class CashTopUpVerifyDto
    {
        public string TrackingNumber { get; set; }
        public decimal AmountToman { get; set; }
    }
}
