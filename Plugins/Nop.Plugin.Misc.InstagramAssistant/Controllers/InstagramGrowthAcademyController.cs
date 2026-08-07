namespace Nop.Plugin.Misc.InstagramAssistant.Controllers
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Plugin.Misc.InstagramAssistant.Services;

    /// <summary>
    /// اولین نقطهٔ ورود واقعی HTTP برای IInstagramGrowthAcademyService — قبلاً این سرویس (محتوای
    /// آموزشی رشد) کامل نوشته شده بود ولی هیچ Controllerی صداش نمی‌زد.
    /// </summary>
    [ApiController]
    [Route("api/instagram/growth-academy")]
    public class InstagramGrowthAcademyController : ControllerBase
    {
        private readonly IInstagramGrowthAcademyService _growthAcademyService;

        public InstagramGrowthAcademyController(IInstagramGrowthAcademyService growthAcademyService)
        {
            _growthAcademyService = growthAcademyService;
        }

        [HttpGet("strategies")]
        public async Task<IActionResult> GetStrategies()
        {
            var strategies = await _growthAcademyService.GetGrowthStrategiesAsync();
            return Ok(new { strategies });
        }

        [HttpGet("campaign-templates")]
        public async Task<IActionResult> GetCampaignTemplates()
        {
            var templates = await _growthAcademyService.GetViralCampaignTemplatesAsync();
            return Ok(new { templates });
        }
    }
}
