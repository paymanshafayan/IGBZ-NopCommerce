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
    using Microsoft.Extensions.Caching.Memory;
    using Microsoft.Extensions.Configuration;
    using Nop.Services.Catalog;
    using Nop.Services.Stores;
    using Nop.Plugin.Misc.InstagramAssistant.Consumers;
    using Nop.Plugin.Misc.InstagramAssistant.Services;
    using Nop.Plugin.Misc.MultiTenantStores.Services;
    /// <summary>
    /// دریافت واقعی Webhook متا برای فیلد «comments» و بستن کامل حلقهٔ «پاسخ خودکار به کامنت»
    /// (نیازمندی #۴ که کاربر مطرح کرد). قبلاً InstagramVipAutomationController.HandleCommentWebhook
    /// این کار را می‌کرد ولی: ۱) هیچ Webhook واقعی صداش نمی‌زد، ۲) دایرکت را واقعاً ارسال نمی‌کرد
    /// (فقط JSON توصیفی برمی‌گرداند)، ۳) تطبیق کلمهٔ کامنت با کد محصول را چک نمی‌کرد، ۴) لایک/پاسخ
    /// عمومی نداشت. این کنترلر همهٔ این‌ها را واقعی می‌کند.
    ///
    /// ⚠️ همان محدودیت‌های واقعی Meta که در InstagramFollowMentionRewardController مستند شده (شکل
    /// دقیق Payload باید با Webhook واقعی تایید شود؛ پنجرهٔ پیام‌رسانی ۲۴ساعته) این‌جا هم صدق می‌کند.
    /// </summary>
    [ApiController]
    [Route("api/instagram/webhook/comments")]
    public class InstagramCommentAutomationController : ControllerBase
    {
        private const string GraphProviderKey = "instagram.graph";
        private static readonly TimeSpan ProcessedCommentCacheTtl = TimeSpan.FromHours(24);

        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _memoryCache;
        private readonly IInstagramFollowMentionRewardService _storeResolutionService;
        private readonly ITenantIntegrationCredentialService _credentialService;
        private readonly IInstagramMessagingService _messagingService;
        private readonly IProductService _productService;
        private readonly IStoreService _storeService;
        private readonly ITenantPlanService _tenantPlanService;
        private readonly InstagramWalletDonationConsumer _donationConsumer;

        public InstagramCommentAutomationController(
            IConfiguration configuration,
            IMemoryCache memoryCache,
            IInstagramFollowMentionRewardService storeResolutionService,
            ITenantIntegrationCredentialService credentialService,
            IInstagramMessagingService messagingService,
            IProductService productService,
            IStoreService storeService,
            ITenantPlanService tenantPlanService,
            InstagramWalletDonationConsumer donationConsumer)
        {
            _configuration = configuration;
            _memoryCache = memoryCache;
            _storeResolutionService = storeResolutionService;
            _credentialService = credentialService;
            _messagingService = messagingService;
            _productService = productService;
            _storeService = storeService;
            _tenantPlanService = tenantPlanService;
            _donationConsumer = donationConsumer;
        }
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

            InstagramCommentWebhookPayload payload;
            try
            {
                payload = JsonSerializer.Deserialize<InstagramCommentWebhookPayload>(rawBody);
            }
            catch (JsonException)
            {
                return BadRequest();
            }

            if (payload?.Entry == null)
                return Ok();

            foreach (var entry in payload.Entry)
            {
                var storeId = await _storeResolutionService.ResolveStoreIdForBusinessAccountAsync(entry.Id);
                if (storeId == null)
                    continue;

                foreach (var change in entry.Changes ?? new List<InstagramCommentWebhookChange>())
                {
                    if (change.Field != "comments")
                        continue;

                    await ProcessCommentAsync(storeId.Value, change.Value);
                }
            }

            return Ok();
        }

        private async Task ProcessCommentAsync(int storeId, JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Object)
                return;

            var commentId = value.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            var commentText = value.TryGetProperty("text", out var textEl) ? textEl.GetString() : null;
            string commenterIgsid = null;
            string commenterUsername = null;
            if (value.TryGetProperty("from", out var fromEl))
            {
                commenterIgsid = fromEl.TryGetProperty("id", out var fromIdEl) ? fromIdEl.GetString() : null;
                commenterUsername = fromEl.TryGetProperty("username", out var usernameEl) ? usernameEl.GetString() : null;
            }

            if (string.IsNullOrEmpty(commentId) || string.IsNullOrEmpty(commentText) || string.IsNullOrEmpty(commenterIgsid))
                return;

            // جلوگیری از پردازش تکراری همان کامنت (تلاش‌های مجدد Webhook متا رایج است).
            var idempotencyKey = $"processed-comment:{commentId}";
            if (_memoryCache.TryGetValue(idempotencyKey, out _))
                return;
            _memoryCache.Set(idempotencyKey, true, ProcessedCommentCacheTtl);

            var credentials = await _credentialService.GetByStoreIdAsync(storeId);
            var credential = credentials.FirstOrDefault(c => c.ProviderKey == GraphProviderKey && c.IsActive);
            if (credential == null)
                return;

            // دستیار اینستاگرام (حتی نسخهٔ عادی) مخصوص پلن‌های نقره‌ای و طلایی است — پلن برنزی و
            // آزمایشی این قابلیت را ندارند.
            var activePlanForAssistant = await _tenantPlanService.GetActivePlanForStoreAsync(storeId);
            if (activePlanForAssistant == null || !activePlanForAssistant.AllowInstagramAiAssistant)
                return;

            var storeAccessToken = _credentialService.DecryptForActualUse(credential.ApiKey);
            var trimmedText = commentText.Trim();

            // اولویت ۱: الگوی حمایت مالی ($عدد) — طبق نیازمندی «کمک مالی» کاربر.
            // این قابلیت مخصوص پلن طلایی (دستیار اینستاگرام Pro) است.
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmedText, @"^\$\d+$"))
            {
                var activePlan = await _tenantPlanService.GetActivePlanForStoreAsync(storeId);
                if (activePlan == null || !activePlan.AllowInstagramAiAssistantPro)
                    return; // فروشگاه پلن Pro ندارد — این قابلیت برایش غیرفعال است

                // صاحب واقعی فروشگاه از TenantStoreSubscription.OwnerCustomerId خوانده می‌شود
                // (نگاشت واقعی که در بازبینی اول این کد از قلم افتاده بود).
                var subscription = await _tenantPlanService.GetSubscriptionByStoreIdAsync(storeId);
                if (subscription != null && subscription.OwnerCustomerId > 0)
                {
                    await _donationConsumer.ProcessCommentForDonationAsync(
                        storeId, subscription.OwnerCustomerId, commenterIgsid, commenterUsername, trimmedText, commentId);
                }
                return;
            }

            // اولویت ۲: تطبیق دقیق متن کامنت با کد محصول (SKU) — طبق نیازمندی صریح کاربر که کلمهٔ
            // کامنت باید همان کد محصول ثبت‌شده در فروشگاه باشد، نه هر ProductId دلخواه.
            // ⚠️ محدودیت کارایی شناخته‌شده: برای فروشگاه‌های با کاتالوگ خیلی بزرگ، لود کل محصولات
            // به‌ازای هر کامنت بهینه نیست. راه‌حل بهتر برای مقیاس بزرگ‌تر: یک ایندکس/کش
            // SKU → ProductId که فقط هنگام درج/ویرایش محصول به‌روزرسانی شود، نه جست‌وجوی کامل هر بار.
            var allStoreProducts = await _productService.SearchProductsAsync(
                storeId: storeId, visibleIndividuallyOnly: true, pageSize: int.MaxValue);

            var matchedProduct = allStoreProducts.FirstOrDefault(p =>
                !string.IsNullOrEmpty(p.Sku) && string.Equals(p.Sku, trimmedText, StringComparison.OrdinalIgnoreCase));

            if (matchedProduct == null)
                return; // نه الگوی حمایت مالی، نه کد محصول شناخته‌شده — نادیده گرفته می‌شود

            // لایک خودکار (Best-effort — طبق مستندسازی بالای کلاس، endpoint آن قطعی نیست)
            await _messagingService.TryLikeCommentAsync(storeAccessToken, commentId);

            // پاسخ عمومی زیر همان کامنت
            await _messagingService.ReplyToCommentPubliclyAsync(
                storeAccessToken, commentId, "جزئیات محصول براتون دایرکت شد ✨");

            // ساخت لینک خرید امن (HMAC) و ارسال واقعی دایرکت
            var hmacSecret = _configuration["InstagramAssistant:VipLinkHmacSigningSecret"];
            if (string.IsNullOrWhiteSpace(hmacSecret))
                return; // بدون کلید امضا، لینک ناامن ساخته نمی‌شود

            var storeEntity = await _storeService.GetStoreByIdAsync(storeId);
            var subdomain = storeEntity?.Hosts?.Split(',').FirstOrDefault() ?? $"store{storeId}.market.com";

            var expires = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds();
            var rawData = $"igsid={commenterIgsid}&product={matchedProduct.Id}&expires={expires}";
            var signature = GenerateHmacSha256(rawData, hmacSecret);
            var secureBuyUrl = $"https://{subdomain}/vip-checkout?igsid={commenterIgsid}&product={matchedProduct.Id}&expires={expires}&sig={signature}";

            var dmText = $"سلام {commenterUsername} عزیز! 🌹 برای «{matchedProduct.Name}» همین الان اقدام کن:\n{secureBuyUrl}\n(این لینک تا ۱۵ دقیقه معتبره)";
            await _messagingService.SendDirectMessageAsync(storeAccessToken, commenterIgsid, dmText);
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

        private static string GenerateHmacSha256(string data, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
    }

    internal class InstagramCommentWebhookPayload
    {
        [JsonPropertyName("object")] public string Object { get; set; }
        [JsonPropertyName("entry")] public List<InstagramCommentWebhookEntry> Entry { get; set; }
    }

    internal class InstagramCommentWebhookEntry
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("changes")] public List<InstagramCommentWebhookChange> Changes { get; set; }
    }

    internal class InstagramCommentWebhookChange
    {
        [JsonPropertyName("field")] public string Field { get; set; }
        [JsonPropertyName("value")] public JsonElement Value { get; set; }
    }
}
