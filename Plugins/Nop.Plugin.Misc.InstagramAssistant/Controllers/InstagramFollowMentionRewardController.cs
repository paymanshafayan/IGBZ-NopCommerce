namespace Nop.Plugin.Misc.InstagramAssistant.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Configuration;
    using Nop.Plugin.Misc.InstagramAssistant.Services;

    /// <summary>
    /// راهکار «فالو + منشن استوری → کد تخفیف در دایرکت» (نیازمندی #۱۳، راهکار ۱ فایل راهکارهای فالوور).
    /// این Endpoint، Webhook واقعی متا برای فیلد «mentions» را دریافت می‌کند.
    ///
    /// ⚠️ دو محدودیت واقعی مهم که پیاده‌سازی آن‌ها را برعهده ندارد (نه ضعف این کد، بلکه واقعیت
    /// پلتفرم متا در ۲۰۲۶):
    /// ۱) شکل دقیق Payload فیلد mentions (این‌که آیا IGSID کاربر منشن‌کننده مستقیماً در آن هست یا
    ///    باید با یک فراخوانی دوم از media_id استخراج شود) باید با یک Webhook واقعی از داشبورد متا
    ///    (بخش Webhooks → Test) تایید شود؛ TryExtractMentioningUserId چند مسیر محتمل را امتحان
    ///    می‌کند ولی این باید پیش از انتشار نهایی راستی‌آزمایی شود.
    /// ۲) ارسال دایرکت فقط تا ۲۴ ساعت پس از آخرین پیام کاربر به پیج مجاز است (سیاست پیام‌رسانی متا)
    ///    — اگر کاربر هرگز پیام نداده، ارسال ممکن است شکست بخورد.
    /// </summary>
    [ApiController]
    [Route("api/instagram/webhook/mentions")]
    public class InstagramFollowMentionRewardController : ControllerBase
    {
        private readonly IInstagramFollowMentionRewardService _rewardService;
        private readonly IConfiguration _configuration;

        public InstagramFollowMentionRewardController(
            IInstagramFollowMentionRewardService rewardService,
            IConfiguration configuration)
        {
            _rewardService = rewardService;
            _configuration = configuration;
        }

        /// <summary>هندشیک تایید اشتراک Webhook مطابق مستندات متا (باید hub.challenge را عیناً برگرداند).</summary>
        [HttpGet]
        public IActionResult VerifySubscription(
            [FromQuery(Name = "hub.mode")] string mode,
            [FromQuery(Name = "hub.verify_token")] string verifyToken,
            [FromQuery(Name = "hub.challenge")] string challenge)
        {
            var expectedToken = _configuration["InstagramAssistant:WebhookVerifyToken"];
            if (string.IsNullOrWhiteSpace(expectedToken))
                return StatusCode(500, "کلید InstagramAssistant:WebhookVerifyToken در تنظیمات یافت نشد.");

            if (mode == "subscribe" && verifyToken == expectedToken && !string.IsNullOrEmpty(challenge))
                return Content(challenge, "text/plain");

            return Forbid();
        }

        [HttpPost]
        public async Task<IActionResult> ReceiveWebhook()
        {
            Request.EnableBuffering();
            string rawBody;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true))
                rawBody = await reader.ReadToEndAsync();
            Request.Body.Position = 0;

            var signatureHeader = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
            if (!VerifySignature(rawBody, signatureHeader))
                return Unauthorized();

            InstagramWebhookPayload payload;
            try
            {
                payload = JsonSerializer.Deserialize<InstagramWebhookPayload>(rawBody);
            }
            catch (JsonException)
            {
                return BadRequest();
            }

            if (payload?.Entry == null)
                return Ok(); // متا انتظار پاسخ ۲۰۰ دارد حتی اگر چیزی برای پردازش نباشد

            foreach (var entry in payload.Entry)
            {
                var storeId = await _rewardService.ResolveStoreIdForBusinessAccountAsync(entry.Id);
                if (storeId == null)
                    continue; // این Business Account هنوز به هیچ فروشگاهی وصل نشده

                foreach (var change in entry.Changes ?? new List<InstagramWebhookChange>())
                {
                    if (change.Field != "mentions")
                        continue;

                    if (TryExtractMentioningUserId(change.Value, out var mentioningUserIgsid))
                    {
                        TryExtractMediaId(change.Value, out var mediaId);
                        await _rewardService.ProcessMentionAsync(storeId.Value, mentioningUserIgsid, mediaId);
                    }
                }
            }

            return Ok();
        }

        private bool VerifySignature(string rawBody, string signatureHeader)
        {
            var appSecret = _configuration["InstagramAssistant:PlatformMetaAppSecret"];
            if (string.IsNullOrWhiteSpace(appSecret) || string.IsNullOrWhiteSpace(signatureHeader))
                return false;

            const string expectedPrefix = "sha256=";
            if (!signatureHeader.StartsWith(expectedPrefix, StringComparison.Ordinal))
                return false;

            var providedHashHex = signatureHeader[expectedPrefix.Length..];

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
            var computedHashHex = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedHashHex),
                Encoding.UTF8.GetBytes(providedHashHex));
        }

        /// <summary>
        /// چند مسیر محتمل برای استخراج IGSID کاربر منشن‌کننده از payload را امتحان می‌کند — شکل
        /// دقیق باید با یک Webhook واقعی تایید شود (به هشدار بالای کلاس مراجعه کنید).
        /// </summary>
        private static bool TryExtractMentioningUserId(JsonElement value, out string igsid)
        {
            igsid = null;

            if (value.ValueKind != JsonValueKind.Object)
                return false;

            if (value.TryGetProperty("from", out var fromEl) && fromEl.TryGetProperty("id", out var fromIdEl))
            {
                igsid = fromIdEl.GetString();
                return !string.IsNullOrEmpty(igsid);
            }

            if (value.TryGetProperty("sender", out var senderEl) && senderEl.TryGetProperty("id", out var senderIdEl))
            {
                igsid = senderIdEl.GetString();
                return !string.IsNullOrEmpty(igsid);
            }

            if (value.TryGetProperty("user_id", out var userIdEl))
            {
                igsid = userIdEl.GetString();
                return !string.IsNullOrEmpty(igsid);
            }

            return false;
        }

        private static bool TryExtractMediaId(JsonElement value, out string mediaId)
        {
            mediaId = null;
            if (value.ValueKind != JsonValueKind.Object)
                return false;

            if (value.TryGetProperty("media_id", out var mediaIdEl))
            {
                mediaId = mediaIdEl.GetString();
                return !string.IsNullOrEmpty(mediaId);
            }

            if (value.TryGetProperty("media", out var mediaEl) && mediaEl.TryGetProperty("id", out var nestedIdEl))
            {
                mediaId = nestedIdEl.GetString();
                return !string.IsNullOrEmpty(mediaId);
            }

            return false;
        }
    }

    internal class InstagramWebhookPayload
    {
        [JsonPropertyName("object")] public string Object { get; set; }
        [JsonPropertyName("entry")] public List<InstagramWebhookEntry> Entry { get; set; }
    }

    internal class InstagramWebhookEntry
    {
        /// <summary>شناسهٔ Business Account اینستاگرامی که منشن شده — برای مسیریابی به تننت درست استفاده می‌شود.</summary>
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("changes")] public List<InstagramWebhookChange> Changes { get; set; }
    }

    internal class InstagramWebhookChange
    {
        [JsonPropertyName("field")] public string Field { get; set; }
        [JsonPropertyName("value")] public JsonElement Value { get; set; }
    }
}
