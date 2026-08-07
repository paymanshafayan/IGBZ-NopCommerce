namespace Nop.Plugin.Misc.MultiTenantStores.Controllers.Public
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// اولین نقطهٔ ورود HTTP واقعی برای GamificationAndAffiliateService.SpinWheelOfFortuneAsync —
    /// این سرویس از قبل کامل نوشته شده بود ولی هیچ Controllerی صداش نمی‌زد.
    /// </summary>
    [ApiController]
    [Route("api/gamification")]
    public class GamificationController : ControllerBase
    {
        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly IGamificationAndAffiliateService _gamificationService;

        public GamificationController(
            IWorkContext workContext,
            IStoreContext storeContext,
            IGamificationAndAffiliateService gamificationService)
        {
            _workContext = workContext;
            _storeContext = storeContext;
            _gamificationService = gamificationService;
        }

        [HttpPost("spin-wheel")]
        public async Task<IActionResult> SpinWheel()
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();

            var result = await _gamificationService.SpinWheelOfFortuneAsync(customer.Id, store.Id);
            if (!result.IsSuccess)
                return BadRequest(new { success = false, message = result.Message });

            return Ok(new
            {
                success = true,
                rewardTitle = result.RewardTitle,
                discountCode = result.DiscountCode,
                message = result.Message
            });
        }
    }
}
