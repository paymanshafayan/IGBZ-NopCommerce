namespace Nop.Plugin.Misc.InstagramAssistant.Services
{
    using System;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Threading.Tasks;

    /// <summary>
    /// اقدامات واقعی روی Instagram Graph API که در چند جای پلاگین لازم است (ارسال دایرکت، پاسخ
    /// عمومی به کامنت، لایک کامنت) — قبلاً این منطق داخل InstagramFollowMentionRewardService تکرار
    /// می‌شد؛ حالا در یک سرویس مشترک است تا InstagramCommentAutomationController هم بتواند از همان
    /// کد استفاده کند.
    /// </summary>
    public interface IInstagramMessagingService
    {
        Task<bool> SendDirectMessageAsync(string storeAccessToken, string recipientIgsid, string messageText);

        Task<bool> ReplyToCommentPubliclyAsync(string storeAccessToken, string commentId, string replyText);

        /// <summary>
        /// ⚠️ عدم قطعیت واقعی: مستندات فعلی متا برای «لایک کردن کامنت توسط اکانت کسب‌وکار از طریق
        /// Graph API» به‌صورت عمومی مستند نیست (برخلاف Reply که مستند و پایدار است). این متد تلاش
        /// می‌کند، ولی اگر endpoint واقعی وجود نداشته باشد یا اسم/مسیرش فرق کند، فقط false برمی‌گرداند
        /// و در جریان اصلی اختلالی ایجاد نمی‌کند — پیش از تکیه‌کردن به آن، با یک تست واقعی روی یک
        /// حساب Business واقعی تایید شود.
        /// </summary>
        Task<bool> TryLikeCommentAsync(string storeAccessToken, string commentId);
    }

    public class InstagramMessagingService : IInstagramMessagingService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public InstagramMessagingService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<bool> SendDirectMessageAsync(string storeAccessToken, string recipientIgsid, string messageText)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient("InstagramGraphApi");
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

        public async Task<bool> ReplyToCommentPubliclyAsync(string storeAccessToken, string commentId, string replyText)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient("InstagramGraphApi");
                var response = await httpClient.PostAsync(
                    $"https://graph.facebook.com/v19.0/{Uri.EscapeDataString(commentId)}/replies" +
                    $"?message={Uri.EscapeDataString(replyText)}&access_token={Uri.EscapeDataString(storeAccessToken)}",
                    null);

                return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<bool> TryLikeCommentAsync(string storeAccessToken, string commentId)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient("InstagramGraphApi");
                var response = await httpClient.PostAsync(
                    $"https://graph.facebook.com/v19.0/{Uri.EscapeDataString(commentId)}/likes" +
                    $"?access_token={Uri.EscapeDataString(storeAccessToken)}",
                    null);

                return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
