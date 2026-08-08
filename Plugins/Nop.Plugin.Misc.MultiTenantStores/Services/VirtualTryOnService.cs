namespace Nop.Plugin.Misc.MultiTenantStores.Services
{
    using System;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;

    /// <summary>
    /// پرو لباس هوش مصنوعی با مدل متن‌باز **IDM-VTON** (https://github.com/yisol/IDM-VTON) با
    /// روش هیبریدی:
    /// ۱) **حالت محلی (لوکال):** پلتفرم IDM-VTON را روی سرور GPU خودش (یا از طریق یک Wrapper
    ///    سبک HTTP) اجرا می‌کند و Endpoint آن در <see cref="TenantIntegrationCredential.EndpointOverrideUrl"/>
    ///    ثبت می‌شود — هزینهٔ پردازش ریالی/رایگان (فقط هزینهٔ زیرساخت).
    /// ۲) **حالت ابری (فال‌بک):** اگر محلی در دسترس نبود یا پاسخ نداد، به Replicate
    ///    (مدل yisol/idm-vton) با توکن ابری (ApiKey) سقوط می‌کند.
    /// ورودی: عکس مشتری + عکس لباس (URL) — خروجی: تصویر مشتری با لباس پوشیده‌شده.
    ///
    /// ⚠️ نکات مهم برای گرفتن بهترین کیفیت (باید برای کاربر در اپ/فرانت نمایش داده شود):
    /// ۱) پس‌زمینهٔ عکس کاربر: هرچه ساده‌تر و تک‌رنگ‌تر باشد، لبه‌های لباس دقیق‌تر پردازش می‌شوند.
    /// ۲) ژست کاربر: فرد نباید دست‌هایش را جلوی بدنش گره زده باشد؛ بهترین حالت، ایستادن صاف با
    ///    دست‌های کمی باز است.
    /// ۳) کیفیت عکس لباس: عکس لباس باید کاملاً واضح، بدون خطای دید و ترجیحاً به‌صورت تخت
    ///    (Flat-lay) گرفته شده باشد.
    /// </summary>
    public interface IVirtualTryOnService
    {
        Task<VirtualTryOnResult> TryOnAsync(
            int storeId,
            string personImageUrl,
            string garmentImageUrl,
            string garmentDescription = null,
            string category = "upper_body");
    }

    public class VirtualTryOnResult
    {
        public bool IsSuccess { get; set; }
        public string ResultImageUrl { get; set; }

        /// <summary>"local" یا "cloud-replicate" — برای نمایش/گزارش.</summary>
        public string Provider { get; set; }
        public string Message { get; set; }
    }

    public class VirtualTryOnService : IVirtualTryOnService
    {
        private const string ProviderKey = "virtual-tryon";

        // Replicate (ابر) — مدل متن‌باز IDM-VTON
        private const string ReplicateCreateUrl = "https://api.replicate.com/v1/predictions";
        private const string ReplicateModel = "yisol/idm-vton";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ITenantIntegrationCredentialService _credentialService;

        public VirtualTryOnService(
            IHttpClientFactory httpClientFactory,
            ITenantIntegrationCredentialService credentialService)
        {
            _httpClientFactory = httpClientFactory;
            _credentialService = credentialService;
        }

        public async Task<VirtualTryOnResult> TryOnAsync(
            int storeId,
            string personImageUrl,
            string garmentImageUrl,
            string garmentDescription = null,
            string category = "upper_body")
        {
            if (string.IsNullOrWhiteSpace(personImageUrl) || string.IsNullOrWhiteSpace(garmentImageUrl))
                return new VirtualTryOnResult { IsSuccess = false, Message = "عکس مشتری و عکس لباس الزامی است." };

            var credential = (await _credentialService.GetByStoreIdAsync(storeId))
                .FirstOrDefault(c => c.ProviderKey == ProviderKey && c.IsActive);

            if (credential == null)
                return new VirtualTryOnResult { IsSuccess = false, Message = "سرویس پرو لباس برای این فروشگاه فعال نشده است." };

            var localEndpoint = credential.EndpointOverrideUrl;
            var cloudToken = _credentialService.DecryptForActualUse(credential.ApiKey);
            var localSecret = _credentialService.DecryptForActualUse(credential.ApiSecret);

            // ۱) حالت محلی (لوکال) — اگر Endpoint محلی تنظیم شده باشد
            if (!string.IsNullOrWhiteSpace(localEndpoint))
            {
                var localResult = await TryLocalAsync(localEndpoint, localSecret, personImageUrl, garmentImageUrl, garmentDescription, category);
                if (localResult.IsSuccess)
                    return localResult;

                // در صورت شکست محلی، به ابری سقوط می‌کنیم (اگر توکن ابری موجود باشد)
            }

            // ۲) حالت ابری (فال‌بک)
            if (!string.IsNullOrWhiteSpace(cloudToken))
            {
                var cloudResult = await TryCloudReplicateAsync(cloudToken, personImageUrl, garmentImageUrl, garmentDescription, category);
                if (cloudResult.IsSuccess)
                    return cloudResult;
            }

            return new VirtualTryOnResult
            {
                IsSuccess = false,
                Message = "پرو لباس در دسترس نیست (هر دو حالت محلی و ابری ناموفق بودند). لطفاً بعداً دوباره تلاش کنید."
            };
        }

        // ────────────────────────── حالت محلی (لوکال) ──────────────────────────

        /// <summary>
        /// فراخوانی Endpoint محلی IDM-VTON (Wrapper سبک HTTP روی مدل):
        /// POST {endpoint} ← { person_image_url, garment_image_url, garment_description, category }
        /// پاسخ: { success: true, result_url: "..." } یا { success: false, message: "..." }
        ///
        /// ⚠️ برای بهترین کیفیت، عکس‌های ورودی باید طبق نکات زیر باشند:
        /// ۱) عکس کاربر: پس‌زمینهٔ ساده/تک‌رنگ + ایستادن صاف با دست‌های کمی باز (نه گره‌خورده جلوی بدن).
        /// ۲) عکس لباس: کاملاً واضح، بدون خطای دید و ترجیحاً تخت (Flat-lay).
        /// </summary>
        private async Task<VirtualTryOnResult> TryLocalAsync(
            string endpoint,
            string localSecret,
            string personImageUrl,
            string garmentImageUrl,
            string garmentDescription,
            string category)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var client = _httpClientFactory.CreateClient("VirtualTryOn");

                // اگر توکن محلی (ApiSecret) تنظیم شده بود، به‌عنوان Authorization ارسال می‌شود
                if (!string.IsNullOrWhiteSpace(localSecret))
                {
                    client.DefaultRequestHeaders.Remove("Authorization");
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {localSecret}");
                }

                var response = await client.PostAsJsonAsync(endpoint, new
                {
                    person_image_url = personImageUrl,
                    garment_image_url = garmentImageUrl,
                    garment_description = garmentDescription,
                    category
                }, cts.Token);

                if (!response.IsSuccessStatusCode)
                    return new VirtualTryOnResult { IsSuccess = false, Message = $"سرور محلی پرو لباس خطا داد (کد {(int)response.StatusCode})." };

                var payload = await response.Content.ReadFromJsonAsync<LocalTryOnResponse>(cts.Token);
                if (payload != null && payload.Success && !string.IsNullOrWhiteSpace(payload.ResultUrl))
                {
                    return new VirtualTryOnResult
                    {
                        IsSuccess = true,
                        ResultImageUrl = payload.ResultUrl,
                        Provider = "local",
                        Message = "پرو لباس با موفقیت روی سرور محلی انجام شد."
                    };
                }

                return new VirtualTryOnResult { IsSuccess = false, Message = payload?.Message ?? "پاسخ نامعتبر از سرور محلی پرو لباس." };
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return new VirtualTryOnResult { IsSuccess = false, Message = $"سرور محلی پرو لباس در دسترس نیست: {ex.Message}" };
            }
        }

        // ────────────────────────── حالت ابری (Replicate) ──────────────────────────

        /// <summary>
        /// فراخوانی مدل yisol/idm-vton روی Replicate (دو مرحله: ساخت Prediction + Polling نتیجه).
        /// </summary>
        private async Task<VirtualTryOnResult> TryCloudReplicateAsync(
            string cloudToken,
            string personImageUrl,
            string garmentImageUrl,
            string garmentDescription,
            string category)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("ReplicateApi");
                client.DefaultRequestHeaders.Remove("Authorization");
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {cloudToken}");

                var createResponse = await client.PostAsJsonAsync(ReplicateCreateUrl, new
                {
                    model = ReplicateModel,
                    input = new
                    {
                        human_img = personImageUrl,
                        garm_img = garmentImageUrl,
                        garment_des = garmentDescription ?? "photo",
                        category
                    }
                });

                if (!createResponse.IsSuccessStatusCode)
                    return new VirtualTryOnResult { IsSuccess = false, Message = $"Replicate درخواست را رد کرد (کد {(int)createResponse.StatusCode})." };

                var created = await createResponse.Content.ReadFromJsonAsync<ReplicatePrediction>();
                if (created?.Id == null)
                    return new VirtualTryOnResult { IsSuccess = false, Message = "پاسخ نامعتبر از Replicate." };

                // Polling نتیجه (حداکثر ~۲ دقیقه)
                for (var i = 0; i < 40; i++)
                {
                    await Task.Delay(3000);

                    var pollResponse = await client.GetAsync($"{ReplicateCreateUrl}/{created.Id}");
                    if (!pollResponse.IsSuccessStatusCode)
                        continue;

                    var prediction = await pollResponse.Content.ReadFromJsonAsync<ReplicatePrediction>();
                    if (prediction?.Status == "succeeded")
                    {
                        var outputUrl = ExtractOutputUrl(prediction.Output);
                        if (!string.IsNullOrWhiteSpace(outputUrl))
                        {
                            return new VirtualTryOnResult
                            {
                                IsSuccess = true,
                                ResultImageUrl = outputUrl,
                                Provider = "cloud-replicate",
                                Message = "پرو لباس با موفقیت روی Replicate انجام شد."
                            };
                        }

                        return new VirtualTryOnResult { IsSuccess = false, Message = "Replicate نتیجه‌ای بدون URL برگرداند." };
                    }

                    if (prediction?.Status is "failed" or "canceled")
                        return new VirtualTryOnResult { IsSuccess = false, Message = "پردازش ابری ناموفق بود." };
                }

                return new VirtualTryOnResult { IsSuccess = false, Message = "زمان‌بندی پردازش ابری به پایان رسید." };
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return new VirtualTryOnResult { IsSuccess = false, Message = $"سرویس ابری پرو لباس در دسترس نیست: {ex.Message}" };
            }
        }

        private static string ExtractOutputUrl(JsonElement output)
        {
            if (output.ValueKind == JsonValueKind.String)
                return output.GetString();

            if (output.ValueKind == JsonValueKind.Array && output.GetArrayLength() > 0)
            {
                var first = output[0];
                if (first.ValueKind == JsonValueKind.String)
                    return first.GetString();
                if (first.ValueKind == JsonValueKind.Object &&
                    first.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String)
                    return urlEl.GetString();
            }

            if (output.ValueKind == JsonValueKind.Object &&
                output.TryGetProperty("url", out var objUrl) && objUrl.ValueKind == JsonValueKind.String)
                return objUrl.GetString();

            return null;
        }
    }

    /// <summary>پاسخ Endpoint محلی IDM-VTON.</summary>
    internal class LocalTryOnResponse
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("result_url")] public string ResultUrl { get; set; }
        [JsonPropertyName("message")] public string Message { get; set; }
    }

    /// <summary>پاسخ Replicate (Prediction).</summary>
    internal class ReplicatePrediction
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; }
        [JsonPropertyName("output")] public JsonElement Output { get; set; }
        [JsonPropertyName("error")] public object Error { get; set; }
    }
}
