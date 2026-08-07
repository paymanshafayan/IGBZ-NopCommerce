namespace Nop.Plugin.Misc.MultiTenantStores.Controllers.Public
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// API عمومی Affiliate Marketing برای کاربر لاگین‌شده — کد معرف، آمار دعوت‌شده‌ها، درخواست
    /// برداشت. مصرف‌کننده: تب «همکاری در فروش» در اپ مشتری/سایت تننت.
    /// </summary>
    [ApiController]
    [Route("api/affiliate")]
    public class AffiliateController : ControllerBase
    {
        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly IAffiliateMarketingService _affiliateService;

        public AffiliateController(
            IWorkContext workContext,
            IStoreContext storeContext,
            IAffiliateMarketingService affiliateService)
        {
            _workContext = workContext;
            _storeContext = storeContext;
            _affiliateService = affiliateService;
        }

        [HttpGet("my-stats")]
        public async Task<IActionResult> GetMyStats()
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();

            var referralCode = await _affiliateService.GetOrCreateReferralCodeAsync(customer.Id, store.Id);
            var stats = await _affiliateService.GetReferralStatsAsync(customer.Id, store.Id);
            stats.ReferralCode ??= referralCode;

            return Ok(new
            {
                stats.ReferralCode,
                ReferralLink = $"{store.Url.TrimEnd('/')}/?ref={stats.ReferralCode}",
                stats.TotalReferredCustomers,
                stats.TotalEarnedToman,
                stats.AvailableBalanceToman
            });
        }

        [HttpPost("request-withdrawal")]
        public async Task<IActionResult> RequestWithdrawal([FromBody] WithdrawalRequestDto dto)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();

            try
            {
                var request = await _affiliateService.RequestWithdrawalAsync(customer.Id, store.Id, dto.AmountToman, dto.BankAccountInfo);
                return Ok(new { success = true, requestId = request.Id, message = "درخواست تسویه‌حساب ثبت شد و پس از بررسی ادمین پرداخت می‌شود." });
            }
            catch (System.InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }
    }

    public class WithdrawalRequestDto
    {
        public decimal AmountToman { get; set; }
        public string BankAccountInfo { get; set; }
    }
}
