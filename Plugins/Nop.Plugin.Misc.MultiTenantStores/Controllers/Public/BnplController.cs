namespace Nop.Plugin.Misc.MultiTenantStores.Controllers.Public
{
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// اولین نقطهٔ ورود واقعی HTTP برای SnappPayBnplGateway.CheckEligibilityAndInstallmentsAsync —
    /// این سرویس قبلاً کامل نوشته شده بود ولی هیچ Controllerی صداش نمی‌زد.
    /// </summary>
    [ApiController]
    [Route("api/checkout/bnpl")]
    public class BnplController : ControllerBase
    {
        private const string ProviderKey = "snapppay";

        private readonly IStoreContext _storeContext;
        private readonly ITenantIntegrationCredentialService _credentialService;
        private readonly SnappPayBnplGateway _bnplGateway;

        public BnplController(
            IStoreContext storeContext,
            ITenantIntegrationCredentialService credentialService,
            SnappPayBnplGateway bnplGateway)
        {
            _storeContext = storeContext;
            _credentialService = credentialService;
            _bnplGateway = bnplGateway;
        }

        [HttpPost("check-eligibility")]
        public async Task<IActionResult> CheckEligibility([FromBody] BnplEligibilityRequestDto dto)
        {
            var store = await _storeContext.GetCurrentStoreAsync();
            var credentials = await _credentialService.GetByStoreIdAsync(store.Id);
            var credential = credentials.FirstOrDefault(c => c.ProviderKey == ProviderKey && c.IsActive);

            if (credential == null)
                return BadRequest(new { success = false, message = "پرداخت اقساطی (اسنپ‌پی) برای این فروشگاه فعال نشده است." });

            var apiKey = _credentialService.DecryptForActualUse(credential.ApiKey);
            var result = await _bnplGateway.CheckEligibilityAndInstallmentsAsync(
                apiKey, dto.CartTotalToman, dto.CustomerNationalId, dto.CustomerMobile);

            if (!result.IsEligible)
                return Ok(new { success = false, message = result.Message });

            return Ok(new
            {
                success = true,
                approvalReferenceId = result.ApprovalReferenceId,
                monthlyInstallmentAmount = result.MonthlyInstallmentAmount,
                installments = result.Installments,
                message = result.Message
            });
        }
    }

    public class BnplEligibilityRequestDto
    {
        public decimal CartTotalToman { get; set; }
        public string CustomerNationalId { get; set; }
        public string CustomerMobile { get; set; }
    }
}
