namespace Nop.Plugin.Api.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Net.Http.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using Nop.Core;
    using Nop.Data;

    public interface IFcmService
    {
        Task RegisterAdminTokenAsync(int adminCustomerId, int storeId, string fcmToken, string deviceName);
        Task<FcmSendResult> SendNotificationToStoreAdminsAsync(int storeId, string title, string body, IDictionary<string, string> dataPayload);
    }

    /// <summary>
    /// یکپارچه‌سازی واقعی با Firebase Cloud Messaging HTTP v1 API. تعداد واقعی ارسال موفق از
    /// پاسخ FCM برای هر توکن به‌طور جداگانه محاسبه می‌شود (نه یک عدد ثابت فرضی).
    /// </summary>
    public class FcmService : IFcmService
    {
        private readonly IRepository<AdminDeviceToken> _deviceTokenRepository;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _fcmProjectId;
        private readonly Func<Task<string>> _oauthAccessTokenProvider;

        public FcmService(
            IRepository<AdminDeviceToken> deviceTokenRepository,
            IHttpClientFactory httpClientFactory,
            string fcmProjectId,
            Func<Task<string>> oauthAccessTokenProvider)
        {
            _deviceTokenRepository = deviceTokenRepository;
            _httpClientFactory = httpClientFactory;
            _fcmProjectId = fcmProjectId;
            _oauthAccessTokenProvider = oauthAccessTokenProvider;
        }

        public async Task RegisterAdminTokenAsync(int adminCustomerId, int storeId, string fcmToken, string deviceName)
        {
            var existing = (await _deviceTokenRepository.GetAllAsync(q =>
                q.Where(t => t.AdminCustomerId == adminCustomerId && t.FcmToken == fcmToken))).FirstOrDefault();

            if (existing != null)
            {
                existing.LastSeenOnUtc = DateTime.UtcNow;
                existing.DeviceName = deviceName;
                await _deviceTokenRepository.UpdateAsync(existing);
                return;
            }

            await _deviceTokenRepository.InsertAsync(new AdminDeviceToken
            {
                AdminCustomerId = adminCustomerId,
                StoreId = storeId,
                FcmToken = fcmToken,
                DeviceName = deviceName,
                CreatedOnUtc = DateTime.UtcNow,
                LastSeenOnUtc = DateTime.UtcNow
            });
        }

        public async Task<FcmSendResult> SendNotificationToStoreAdminsAsync(int storeId, string title, string body, IDictionary<string, string> dataPayload)
        {
            var tokens = await _deviceTokenRepository.GetAllAsync(q => q.Where(t => t.StoreId == storeId));
            if (!tokens.Any())
                return new FcmSendResult { Success = false, DeliveredCount = 0, Message = "هیچ دستگاه ثبت‌شده‌ای برای این فروشگاه یافت نشد." };

            var accessToken = await _oauthAccessTokenProvider();
            var httpClient = _httpClientFactory.CreateClient("FcmV1");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var deliveredCount = 0;
            var invalidTokenIds = new List<int>();

            foreach (var deviceToken in tokens)
            {
                var response = await httpClient.PostAsJsonAsync(
                    $"https://fcm.googleapis.com/v1/projects/{_fcmProjectId}/messages:send",
                    new FcmSendRequest
                    {
                        Message = new FcmMessage
                        {
                            Token = deviceToken.FcmToken,
                            Notification = new FcmNotification { Title = title, Body = body },
                            Data = dataPayload
                        }
                    });

                if (response.IsSuccessStatusCode)
                {
                    deliveredCount++;
                }
                else if ((int)response.StatusCode == 404 || (int)response.StatusCode == 400)
                {
                    // توکن نامعتبر/منقضی‌شده — باید از دیتابیس حذف شود تا تلاش‌های آینده هدر نروند
                    invalidTokenIds.Add(deviceToken.Id);
                }
            }

            foreach (var tokenId in invalidTokenIds)
            {
                var staleToken = await _deviceTokenRepository.GetByIdAsync(tokenId);
                if (staleToken != null)
                    await _deviceTokenRepository.DeleteAsync(staleToken);
            }

            return new FcmSendResult
            {
                Success = deliveredCount > 0,
                DeliveredCount = deliveredCount,
                Message = $"{deliveredCount} از {tokens.Count} دستگاه با موفقیت اعلان را دریافت کردند."
            };
        }
    }

    public class FcmSendResult
    {
        public bool Success { get; set; }
        public int DeliveredCount { get; set; }
        public string Message { get; set; }
    }

    /// <summary>موجودیت نگهداری توکن دستگاه ادمین برای ارسال Push (قبلاً هیچ‌جا تعریف نشده بود)</summary>
    public class AdminDeviceToken : Nop.Core.BaseEntity
    {
        public int AdminCustomerId { get; set; }
        public int StoreId { get; set; }
        public string FcmToken { get; set; }
        public string DeviceName { get; set; }
        public DateTime CreatedOnUtc { get; set; }
        public DateTime LastSeenOnUtc { get; set; }
    }

    internal class FcmSendRequest
    {
        [JsonPropertyName("message")] public FcmMessage Message { get; set; }
    }

    internal class FcmMessage
    {
        [JsonPropertyName("token")] public string Token { get; set; }
        [JsonPropertyName("notification")] public FcmNotification Notification { get; set; }
        [JsonPropertyName("data")] public IDictionary<string, string> Data { get; set; }
    }

    internal class FcmNotification
    {
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("body")] public string Body { get; set; }
    }
}
