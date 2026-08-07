namespace Nop.Plugin.Misc.InstagramAssistant.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using Nop.Core.Domain.Discounts;
    using Nop.Services.Discounts;
    using Nop.Plugin.Misc.InstagramAssistant.Domain;
    using Nop.Plugin.Misc.MultiTenantStores.Services;
    using Nop.Data;

    public enum FollowMentionRewardStatus
    {
        Rewarded,
        AlreadyRewarded,
        NotFollowing,
        StoreCredentialMissing
    }

    public class FollowMentionRewardResult
    {
        public FollowMentionRewardStatus Status { get; set; }
        public string CouponCode { get; set; }
        public bool DirectMessageSent { get; set; }
    }

    public interface IInstagramFollowMentionRewardService
    {
        /// <summary>
        /// نگاشت شناسهٔ Business Account اینستاگرام (که در entry.id هر رویدادِ Webhook می‌آید) به
        /// StoreId داخلی — لازم چون یک اپ متای مشترک، Webhookهای همهٔ تننت‌ها را به یک Endpoint واحد
        /// می‌فرستد. اگر تننتی این شناسه را نداشته باشد (هنوز حساب اینستاگرامش را وصل نکرده)، نال برمی‌گردد.
        /// </summary>
        Task<int?> ResolveStoreIdForBusinessAccountAsync(string mentionedBusinessAccountId);

        /// <summary>
        /// پردازش کامل یک رویداد «کاربری استوری فروشگاه را منشن کرد»: بررسی فالو، صدور کد تخفیف
        /// یک‌بارمصرف واقعی nopCommerce، ثبت در دفترکل (برای جلوگیری از تکرار)، و ارسال دایرکت.
        /// </summary>
        Task<FollowMentionRewardResult> ProcessMentionAsync(int storeId, string mentioningUserIgsid, string mediaId);
    }

    public class InstagramFollowMentionRewardService : IInstagramFollowMentionRewardService
    {
        private const string ProviderKey = "instagram.graph";
        private const decimal RewardDiscountPercentage = 15m;

        // کش سطح-پردازه از (Business Account ID اینستاگرام) → StoreId، چون این نگاشت به‌ندرت تغییر
        // می‌کند ولی فراخوانی Graph API برای هر Webhook دریافتی، پرهزینه و غیرضروری است.
        private static readonly ConcurrentDictionary<string, int> BusinessAccountToStoreCache = new();
        private static DateTime _cacheLastRefreshedUtc = DateTime.MinValue;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
        private static readonly SemaphoreSlimWrapper CacheRefreshLock = new();

        private readonly ITenantIntegrationCredentialService _credentialService;
        private readonly IRepository<InstagramFollowMentionReward> _rewardRepository;
        private readonly IDiscountService _discountService;
        private readonly IHttpClientFactory _httpClientFactory;

        public InstagramFollowMentionRewardService(
            ITenantIntegrationCredentialService credentialService,
            IRepository<InstagramFollowMentionReward> rewardRepository,
            IDiscountService discountService,
            IHttpClientFactory httpClientFactory)
        {
            _credentialService = credentialService;
            _rewardRepository = rewardRepository;
            _discountService = discountService;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<int?> ResolveStoreIdForBusinessAccountAsync(string mentionedBusinessAccountId)
        {
            if (string.IsNullOrWhiteSpace(mentionedBusinessAccountId))
                return null;

            if (BusinessAccountToStoreCache.TryGetValue(mentionedBusinessAccountId, out var cachedStoreId)
                && DateTime.UtcNow - _cacheLastRefreshedUtc < CacheTtl)
                return cachedStoreId;

            await RefreshBusinessAccountCacheAsync();

            return BusinessAccountToStoreCache.TryGetValue(mentionedBusinessAccountId, out var storeId) ? storeId : null;
        }

        private async Task RefreshBusinessAccountCacheAsync()
        {
            await CacheRefreshLock.WaitAsync();
            try
            {
                // ممکن است پردازه‌ای دیگر همین را در حین انتظار برای Lock رفرش کرده باشد.
                if (DateTime.UtcNow - _cacheLastRefreshedUtc < CacheTtl)
                    return;

                var allCredentials = await _credentialService.GetAllActiveByProviderKeyAsync(ProviderKey);
                var httpClient = _httpClientFactory.CreateClient("InstagramGraphApi");

                foreach (var credential in allCredentials)
                {
                    try
                    {
                        var token = _credentialService.DecryptForActualUse(credential.ApiKey);
                        var response = await httpClient.GetAsync(
                            $"https://graph.instagram.com/me?fields=id&access_token={Uri.EscapeDataString(token)}");

                        if (!response.IsSuccessStatusCode) continue;

                        var payload = await response.Content.ReadFromJsonAsync<InstagramMeIdResponse>();
                        if (!string.IsNullOrWhiteSpace(payload?.Id))
                            BusinessAccountToStoreCache[payload.Id] = credential.StoreId;
                    }
                    catch (Exception)
                    {
                        // یک فروشگاه با توکن منقضی نباید رفرش کل کش را متوقف کند — رد شو و بعدی را امتحان کن.
                    }
                }

                _cacheLastRefreshedUtc = DateTime.UtcNow;
            }
            finally
            {
                CacheRefreshLock.Release();
            }
        }

        public async Task<FollowMentionRewardResult> ProcessMentionAsync(int storeId, string mentioningUserIgsid, string mediaId)
        {
            var existing = await _rewardRepository.GetAllAsync(q =>
                q.Where(r => r.StoreId == storeId && r.InstagramScopedId == mentioningUserIgsid));

            var alreadyRewarded = existing.FirstOrDefault();
            if (alreadyRewarded != null)
                return new FollowMentionRewardResult
                {
                    Status = FollowMentionRewardStatus.AlreadyRewarded,
                    CouponCode = alreadyRewarded.CouponCode,
                    DirectMessageSent = alreadyRewarded.DirectMessageSent
                };

            var credentials = await _credentialService.GetByStoreIdAsync(storeId);
            var credential = credentials.FirstOrDefault(c => c.ProviderKey == ProviderKey && c.IsActive);
            if (credential == null)
                return new FollowMentionRewardResult { Status = FollowMentionRewardStatus.StoreCredentialMissing };

            var storeAccessToken = _credentialService.DecryptForActualUse(credential.ApiKey);
            var httpClient = _httpClientFactory.CreateClient("InstagramGraphApi");

            // بررسی واقعی فالو بودن. ⚠️ این فیلد (is_user_follow_business) بخشی از Instagram Messaging
            // Insights است و طبق مستندات متا فقط برای کاربرانی معتبر است که اخیراً با پیج پیام‌رسانی
            // داشته‌اند — قبل از فعال‌سازی نهایی، این رفتار باید با یک Payload واقعی از داشبورد متا تایید شود.
            var followCheckResponse = await httpClient.GetAsync(
                $"https://graph.instagram.com/{Uri.EscapeDataString(mentioningUserIgsid)}" +
                $"?fields=is_user_follow_business&access_token={Uri.EscapeDataString(storeAccessToken)}");

            var isFollowing = false;
            if (followCheckResponse.IsSuccessStatusCode)
            {
                var followPayload = await followCheckResponse.Content.ReadFromJsonAsync<InstagramFollowCheckResponse>();
                isFollowing = followPayload?.IsUserFollowBusiness ?? false;
            }

            if (!isFollowing)
                return new FollowMentionRewardResult { Status = FollowMentionRewardStatus.NotFollowing };

            var couponCode = $"IGFM-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

            // ⚠️ فیلدهای دقیق Discount باید بعد از build واقعی با نسخهٔ nopCommerce 4.90.6 تایید شوند؛
            // این بخش (برخلاف بقیهٔ سرویس‌های پروژه) هیچ نمونهٔ قبلی در کدبیس نداشت.
            var discount = new Discount
            {
                Name = $"پاداش فالو+منشن اینستاگرام - {mentioningUserIgsid}",
                DiscountTypeId = (int)DiscountType.AssignedToOrderTotal,
                UsePercentage = true,
                DiscountPercentage = RewardDiscountPercentage,
                RequiresCouponCode = true,
                CouponCode = couponCode,
                IsCumulative = false,
                DiscountLimitationId = (int)DiscountLimitationType.NTimesOnly,
                LimitationTimes = 1,
                IsActive = true
            };
            await _discountService.InsertDiscountAsync(discount);

            var directMessageSent = await SendDirectMessageAsync(httpClient, storeAccessToken, mentioningUserIgsid, couponCode);

            var fallbackCommentPosted = false;
            if (!directMessageSent)
                fallbackCommentPosted = await PostFallbackCommentAsync(httpClient, storeAccessToken, mediaId);

            var reward = new InstagramFollowMentionReward
            {
                StoreId = storeId,
                InstagramScopedId = mentioningUserIgsid,
                CouponCode = couponCode,
                DirectMessageSent = directMessageSent,
                FallbackCommentPosted = fallbackCommentPosted,
                IssuedOnUtc = DateTime.UtcNow
            };
            await _rewardRepository.InsertAsync(reward);

            return new FollowMentionRewardResult
            {
                Status = FollowMentionRewardStatus.Rewarded,
                CouponCode = couponCode,
                DirectMessageSent = directMessageSent
            };
        }

        /// <summary>
        /// وقتی دایرکت به‌خاطر پنجرهٔ پیام‌رسانی ۲۴ساعتهٔ متا شکست بخورد، به‌جای آن یک کامنت عمومی
        /// زیر همان پست/استوری منشن‌شده گذاشته می‌شود که از کاربر می‌خواهد برای دریافت کد به پیج
        /// دایرکت بدهد. کد تخفیف واقعی عمداً در کامنت عمومی درج نمی‌شود — چون کامنت‌ها برای همه قابل
        /// مشاهده‌اند و درج کد در آن‌جا یعنی هر کسی (نه فقط کاربری که واقعاً فالو+منشن کرده) می‌تواند
        /// از کد استفاده کند.
        /// </summary>
        private async Task<bool> PostFallbackCommentAsync(HttpClient httpClient, string storeAccessToken, string mediaId)
        {
            if (string.IsNullOrWhiteSpace(mediaId))
                return false;

            try
            {
                const string commentText = "🎉 خبر خوب داریم برات! لطفاً یه دایرکت برامون بفرست تا کد تخفیفتو برات ارسال کنیم.";

                var response = await httpClient.PostAsync(
                    $"https://graph.facebook.com/v19.0/{Uri.EscapeDataString(mediaId)}/comments" +
                    $"?message={Uri.EscapeDataString(commentText)}&access_token={Uri.EscapeDataString(storeAccessToken)}",
                    null);

                return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// ⚠️ محدودیت سیاست پیام‌رسانی متا: ارسال پیام آزاد (خارج از Message Tag مجاز) فقط تا ۲۴
        /// ساعت پس از آخرین پیامی که همان کاربر به پیج فرستاده مجاز است. اگر کاربر هرگز به پیج پیام
        /// نداده باشد (که در سناریوی «فقط منشن استوری» محتمل است)، این فراخوانی ممکن است با خطای
        /// سیاست‌گذاری متا شکست بخورد — باید یک مسیر جایگزین (مثل کامنت خودکار زیر همان استوری) هم
        /// در نظر گرفته شود.
        /// </summary>
        private async Task<bool> SendDirectMessageAsync(HttpClient httpClient, string storeAccessToken, string recipientIgsid, string couponCode)
        {
            try
            {
                var messageText = $"سلام! ممنون که استوری ما رو منشن کردی 🎉 کد تخفیف اختصاصی‌ات: {couponCode}";

                var response = await httpClient.PostAsJsonAsync(
                    $"https://graph.facebook.com/v19.0/me/messages?access_token={Uri.EscapeDataString(storeAccessToken)}",
                    new
                    {
                        recipient = new { id = recipientIgsid },
                        message = new { text = messageText }
                    });

                return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    internal class InstagramMeIdResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; }
    }

    internal class InstagramFollowCheckResponse
    {
        [JsonPropertyName("is_user_follow_business")] public bool IsUserFollowBusiness { get; set; }
    }

    /// <summary>Wrapper سبک به‌جای وابستگی مستقیم به SemaphoreSlim برای جلوگیری از رفرش هم‌زمان کش در چند Request موازی.</summary>
    internal class SemaphoreSlimWrapper
    {
        private readonly System.Threading.SemaphoreSlim _semaphore = new(1, 1);
        public Task WaitAsync() => _semaphore.WaitAsync();
        public void Release() => _semaphore.Release();
    }
}
