namespace Nop.Plugin.Misc.MultiTenantStores.Controllers.Public
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// Endpointهای عمومی فید محصولات — طبق راهنمای «اتصال فروشگاه به ترب و دیجی‌کالا» و
    /// «سئو و تبلیغات»: کاربر این آدرس‌ها را یک‌بار در پنل ترب/یکتانت کپی می‌کند و از آن به بعد
    /// همه‌چیز خودکار است. نسخهٔ قبلی سرویس‌های تولید این فید وجود داشتند اما هیچ Controller‌ای
    /// آن‌ها را در دسترس قرار نداده بود.
    /// </summary>
    [Route("feeds")]
    public class MarketplaceFeedsController : Controller
    {
        private readonly IMarketplaceOmnichannelService _marketplaceService;
        private readonly ISeoAndAdNetworksFeedService _seoFeedService;

        public MarketplaceFeedsController(
            IMarketplaceOmnichannelService marketplaceService,
            ISeoAndAdNetworksFeedService seoFeedService)
        {
            _marketplaceService = marketplaceService;
            _seoFeedService = seoFeedService;
        }

        /// <summary>خروجی JSON فید ترب برای فروشگاه مشخص‌شده — این لینک را کاربر در پنل ترب وارد می‌کند</summary>
        [HttpGet("torob/{storeId}.json")]
        public async Task<IActionResult> TorobFeed(int storeId, int page = 1, int pageSize = 200)
        {
            var feed = await _marketplaceService.GetTorobLiveJsonFeedAsync(storeId, page, pageSize);
            return Json(feed);
        }

        /// <summary>خروجی XML فید یکتانت/تپسل برای فروشگاه مشخص‌شده</summary>
        [HttpGet("yektanet/{storeId}.xml")]
        public async Task<IActionResult> YektanetFeed(int storeId)
        {
            var xml = await _seoFeedService.GenerateYektanetRetargetingXmlFeedAsync(storeId);
            return Content(xml, "application/xml");
        }
    }
}
