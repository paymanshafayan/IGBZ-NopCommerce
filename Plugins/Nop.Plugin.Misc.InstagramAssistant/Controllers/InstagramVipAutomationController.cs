namespace Nop.Plugin.Misc.InstagramAssistant.Controllers
{
    using System;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Services.Catalog;
    using Nop.Plugin.Misc.InstagramAssistant.Services;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    [ApiController]
    [Route("api/instagram/vip")]
    public class InstagramVipAutomationController : ControllerBase
    {
        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly IProductService _productService;
        private readonly IInstagramCustomerLinkService _customerLinkService;
        private readonly ILmsAndVideoSecurityService _videoSecurityService;
        private readonly ITenantPlanService _tenantPlanService;
        private readonly string _hmacSigningSecret;

        public InstagramVipAutomationController(
            IWorkContext workContext,
            IStoreContext storeContext,
            IProductService productService,
            IInstagramCustomerLinkService customerLinkService,
            ILmsAndVideoSecurityService videoSecurityService,
            ITenantPlanService tenantPlanService,
            Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            _workContext = workContext;
            _storeContext = storeContext;
            _productService = productService;
            _customerLinkService = customerLinkService;
            _videoSecurityService = videoSecurityService;
            _tenantPlanService = tenantPlanService;

            // کلید امضا هرگز نباید Hardcode باشد (باگ امنیتی نسخهٔ قبلی) — از تنظیمات خوانده می‌شود.
            _hmacSigningSecret = configuration["InstagramAssistant:VipLinkHmacSigningSecret"]
                ?? throw new InvalidOperationException(
                    "کلید InstagramAssistant:VipLinkHmacSigningSecret در تنظیمات پیدا نشد.");
        }

        /// <summary>
        /// ⚠️ منسوخ: این اکشن قبلاً این‌جا بود ولی سه نقص واقعی داشت — پیام دایرکت را واقعاً ارسال
        /// نمی‌کرد (فقط JSON توصیفی برمی‌گرداند)، تطبیق SKU را چک نمی‌کرد، و هیچ Webhook واقعی متا
        /// صداش نمی‌زد. جایگزین واقعی: InstagramCommentAutomationController
        /// (api/instagram/webhook/comments) که واقعاً دایرکت می‌فرستد، لایک/پاسخ عمومی می‌گذارد، و
        /// به یک Webhook واقعی وصل است.
        /// </summary>

        /// <summary>
        /// اعتبارسنجی لینک یک‌بارمصرف و صدور توکن پخش امن واقعی از طریق ILmsAndVideoSecurityService
        /// (به‌جای تولید توکن غیرامضاشدهٔ محلی که در نسخهٔ قبلی وجود داشت).
        /// </summary>
        [HttpGet("video-access-token")]
        public async Task<IActionResult> GetVipVideoToken([FromQuery] string igsid, [FromQuery] int lessonId, [FromQuery] long expires, [FromQuery] string sig)
        {
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expires)
                return StatusCode(403, "لینک دسترسی منقضی شده است. لطفاً مجدداً از دایرکت اقدام کنید.");

            var rawData = $"igsid={igsid}&product={lessonId}&expires={expires}";
            var expectedSig = GenerateHmacSha256(rawData, _hmacSigningSecret);

            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(sig ?? string.Empty),
                    Encoding.UTF8.GetBytes(expectedSig)))
                return StatusCode(403, "امضای دیجیتال لینک نامعتبر است!");

            var customer = await _customerLinkService.GetCustomerByInstagramScopedIdAsync(igsid);
            if (customer == null)
                return StatusCode(403, "حساب کاربری متصل به این شناسهٔ اینستاگرام یافت نشد.");

            // قابلیت «مشتریان VIP برای ویدیوهای اشتراکی» مخصوص پلن طلایی (Instagram Assistant Pro)
            // است — طبق ساختار پلن‌های سایت مادر. اگر فروشگاه این پلن را نداشته باشد، ولو لینک واقعی
            // و امضایش معتبر باشد، دسترسی داده نمی‌شود.
            var currentStore = await _storeContext.GetCurrentStoreAsync();
            var activePlan = await _tenantPlanService.GetActivePlanForStoreAsync(currentStore.Id);
            if (activePlan == null || !activePlan.AllowInstagramAiAssistantPro)
                return StatusCode(402, "این قابلیت مخصوص پلن طلایی (دستیار اینستاگرام Pro) است. لطفاً پلن فروشگاه را ارتقا دهید.");

            var userIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var result = await _videoSecurityService.GetWatermarkedCourseVideoUrlAsync(
                courseId: 0, // ویدیوهای VIP اینستاگرام دوره مستقل ندارند؛ فقط بر اساس lessonId شناسایی می‌شوند
                lessonId: lessonId,
                customerId: customer.Id,
                userPhoneNumber: customer.Phone,
                userIpAddress: userIp,
                validFor: TimeSpan.FromMinutes(20));

            return Ok(new
            {
                VideoEmbedUrl = result.EmbedPlayerUrl,
                ExpiresOnUtc = result.ExpiresOnUtc,
                AllowDownload = false
            });
        }

        private static string GenerateHmacSha256(string data, string key)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
