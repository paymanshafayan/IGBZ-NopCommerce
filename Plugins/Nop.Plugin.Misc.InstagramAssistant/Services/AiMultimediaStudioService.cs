namespace Nop.Plugin.Misc.InstagramAssistant.Services
{
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;

    /// <summary>
    /// AI Multimedia Content Studio Service (.NET 9 / nopCommerce 4.90)
    /// عکس‌برداری استودیویی، ویدیوی کوتاه استوری و صداپیشگی گوینده فارسی — همه از طریق فراخوانی
    /// واقعی سرویس‌های AI بیرونی (دیپ‌فا/آتنا/ویرا). خروجی هرگز با دستکاری رشتهٔ URL ورودی
    /// (مثلاً .jpg -> _studio_4k.jpg) جعل نمی‌شود، چون آن فایل هرگز واقعاً وجود نخواهد داشت.
    /// </summary>
    public interface IAiMultimediaStudioService
    {
        Task<AiImageStudioResult> EnhanceProductPhotoAsync(string aiProviderApiKey, string rawImageUrl, string backgroundPreset, bool applySkuWatermark, string skuCode);
        Task<AiVideoStoryResult> Generate5SecProductVideoStoryAsync(string aiProviderApiKey, string productId, string productTitle, decimal priceToman, string backgroundMusicTrackId = null);
        Task<AiVoiceOverResult> GeneratePersianVoiceOverAsync(string ttsProviderApiKey, string textToSpeak, string speakerVoiceGender = "Female");

        /// <summary>
        /// تولید عکس مدل/آواتار انسانی به همراه محصول — طبق نیازمندی کاربر («تصویر یا ویدئوی یک
        /// خانم که محصول را در دست دارد»). <paramref name="productImageUrl"/> اختیاری است: اگر خالی
        /// باشد، تصویر کاملاً از روی توضیح متنی ساخته می‌شود (بدون نیاز به عکس محصول).
        /// </summary>
        Task<AiImageStudioResult> GenerateModelPhotoAsync(string aiProviderApiKey, string modelDescription, string productImageUrl, bool applySkuWatermark, string skuCode);

        /// <summary>نسخهٔ ویدیویی همان قابلیت بالا — با یا بدون عکس محصول.</summary>
        Task<AiVideoStoryResult> GenerateModelVideoAsync(string aiProviderApiKey, string modelDescription, string productImageUrl, string backgroundMusicTrackId = null);
    }

    public class AiMultimediaStudioService : IAiMultimediaStudioService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public AiMultimediaStudioService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<AiImageStudioResult> EnhanceProductPhotoAsync(string aiProviderApiKey, string rawImageUrl, string backgroundPreset, bool applySkuWatermark, string skuCode)
        {
            if (string.IsNullOrWhiteSpace(aiProviderApiKey))
                return new AiImageStudioResult { IsSuccess = false, Message = "کلید API استودیوی تصویر هوش مصنوعی تنظیم نشده است." };

            var httpClient = _httpClientFactory.CreateClient("AiImageStudio");
            httpClient.DefaultRequestHeaders.Remove("Authorization");
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {aiProviderApiKey}");

            var response = await httpClient.PostAsJsonAsync("https://api.ai-image-studio-provider.local/v1/enhance", new
            {
                source_url = rawImageUrl,
                background_preset = backgroundPreset,
                apply_sku_watermark = applySkuWatermark,
                sku_code = skuCode
            });

            if (!response.IsSuccessStatusCode)
                return new AiImageStudioResult { IsSuccess = false, Message = $"سرویس ادیت تصویر خطا داد (کد {(int)response.StatusCode})." };

            var payload = await response.Content.ReadFromJsonAsync<AiImageStudioApiResponse>();
            return new AiImageStudioResult
            {
                IsSuccess = payload?.Success ?? false,
                EnhancedImageUrl = payload?.ResultUrl,
                SkuWatermarkApplied = applySkuWatermark,
                SkuCode = skuCode,
                Message = payload?.Success == true ? "تصویر محصول با موفقیت پردازش شد." : "پردازش تصویر توسط سرویس ناموفق بود."
            };
        }

        public async Task<AiVideoStoryResult> Generate5SecProductVideoStoryAsync(string aiProviderApiKey, string productId, string productTitle, decimal priceToman, string backgroundMusicTrackId = null)
        {
            if (string.IsNullOrWhiteSpace(aiProviderApiKey))
                return new AiVideoStoryResult { IsSuccess = false, Message = "کلید API استودیوی ویدیو تنظیم نشده است." };

            var httpClient = _httpClientFactory.CreateClient("AiVideoStudio");
            httpClient.DefaultRequestHeaders.Remove("Authorization");
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {aiProviderApiKey}");

            // ⚠️ افزودن موسیقی پس‌زمینه (نیازمندی #۱: «AI ساخت پست تصویر+آهنگ») به‌صورت پارامتر واقعی
            // به درخواست اضافه شد. چون Endpoint این سرویس هنوز نمادین است (سند ممیزی، بند ۲) و
            // مستندات واقعی API آتنا در دسترس نیست، نمی‌شود مطمئن بود نام فیلد صحیح
            // "background_music_track_id" است یا این سرویس اصلاً چنین قابلیتی دارد — این باید با
            // مستندات واقعی provider راستی‌آزمایی شود.
            var response = await httpClient.PostAsJsonAsync("https://api.ai-video-studio-provider.local/v1/story", new
            {
                product_id = productId,
                title = productTitle,
                price_toman = priceToman,
                background_music_track_id = backgroundMusicTrackId
            });

            if (!response.IsSuccessStatusCode)
                return new AiVideoStoryResult { IsSuccess = false, Message = $"سرویس تولید ویدیو خطا داد (کد {(int)response.StatusCode})." };

            var payload = await response.Content.ReadFromJsonAsync<AiVideoStudioApiResponse>();
            return new AiVideoStoryResult
            {
                IsSuccess = payload?.Success ?? false,
                VideoStoryUrl = payload?.VideoUrl,
                DurationSeconds = payload?.DurationSeconds ?? 0,
                BackgroundMusicTrackId = backgroundMusicTrackId,
                Message = payload?.Success == true ? "ویدیوی استوری محصول با موفقیت تولید شد." : "تولید ویدیو ناموفق بود."
            };
        }

        public async Task<AiVoiceOverResult> GeneratePersianVoiceOverAsync(string ttsProviderApiKey, string textToSpeak, string speakerVoiceGender = "Female")
        {
            if (string.IsNullOrWhiteSpace(ttsProviderApiKey))
                return new AiVoiceOverResult { IsSuccess = false, Message = "کلید API سرویس تبدیل متن به گفتار تنظیم نشده است." };

            var httpClient = _httpClientFactory.CreateClient("AiTtsProvider");
            httpClient.DefaultRequestHeaders.Remove("Authorization");
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ttsProviderApiKey}");

            var response = await httpClient.PostAsJsonAsync("https://api.ai-tts-provider.local/v1/synthesize", new
            {
                text = textToSpeak,
                voice_gender = speakerVoiceGender,
                language = "fa-IR"
            });

            if (!response.IsSuccessStatusCode)
                return new AiVoiceOverResult { IsSuccess = false, Message = $"سرویس تبدیل متن به گفتار خطا داد (کد {(int)response.StatusCode})." };

            var payload = await response.Content.ReadFromJsonAsync<AiTtsApiResponse>();
            return new AiVoiceOverResult
            {
                IsSuccess = payload?.Success ?? false,
                AudioMp3Url = payload?.AudioUrl,
                Speaker = $"گوینده فارسی ({speakerVoiceGender})",
                Message = payload?.Success == true ? "فایل صوتی با موفقیت تولید شد." : "تولید فایل صوتی ناموفق بود."
            };
        }

        public async Task<AiImageStudioResult> GenerateModelPhotoAsync(string aiProviderApiKey, string modelDescription, string productImageUrl, bool applySkuWatermark, string skuCode)
        {
            if (string.IsNullOrWhiteSpace(aiProviderApiKey))
                return new AiImageStudioResult { IsSuccess = false, Message = "کلید API استودیوی تصویر هوش مصنوعی تنظیم نشده است." };

            if (string.IsNullOrWhiteSpace(modelDescription))
                return new AiImageStudioResult { IsSuccess = false, Message = "توضیح مدل/آواتار الزامی است (مثلاً «خانم جوان، استایل ساده و گرم»)." };

            var httpClient = _httpClientFactory.CreateClient("AiImageStudio");
            httpClient.DefaultRequestHeaders.Remove("Authorization");
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {aiProviderApiKey}");

            // ⚠️ Endpoint نمادین است (به تصمیم قبلی کاربر: بدون مستندات واقعی دیپ‌فا، همین‌طور
            // نمادین می‌ماند). product_image_url عمداً می‌تواند خالی باشد — طبق نیازمندی صریح کاربر
            // که باید بتواند «حتی بدون تصویر محصول» تصویر/ویدیوی مدنظرش را بسازد.
            var response = await httpClient.PostAsJsonAsync("https://api.ai-image-studio-provider.local/v1/generate-model", new
            {
                model_description = modelDescription,
                product_image_url = string.IsNullOrWhiteSpace(productImageUrl) ? null : productImageUrl,
                apply_sku_watermark = applySkuWatermark,
                sku_code = skuCode
            });

            if (!response.IsSuccessStatusCode)
                return new AiImageStudioResult { IsSuccess = false, Message = $"سرویس تولید عکس مدل خطا داد (کد {(int)response.StatusCode})." };

            var payload2 = await response.Content.ReadFromJsonAsync<AiImageStudioApiResponse>();
            return new AiImageStudioResult
            {
                IsSuccess = payload2?.Success ?? false,
                EnhancedImageUrl = payload2?.ResultUrl,
                SkuWatermarkApplied = applySkuWatermark,
                SkuCode = skuCode,
                Message = payload2?.Success == true ? "عکس مدل با موفقیت تولید شد." : "تولید عکس مدل ناموفق بود."
            };
        }

        public async Task<AiVideoStoryResult> GenerateModelVideoAsync(string aiProviderApiKey, string modelDescription, string productImageUrl, string backgroundMusicTrackId = null)
        {
            if (string.IsNullOrWhiteSpace(aiProviderApiKey))
                return new AiVideoStoryResult { IsSuccess = false, Message = "کلید API استودیوی ویدیو تنظیم نشده است." };

            if (string.IsNullOrWhiteSpace(modelDescription))
                return new AiVideoStoryResult { IsSuccess = false, Message = "توضیح مدل/آواتار الزامی است." };

            var httpClient2 = _httpClientFactory.CreateClient("AiVideoStudio");
            httpClient2.DefaultRequestHeaders.Remove("Authorization");
            httpClient2.DefaultRequestHeaders.Add("Authorization", $"Bearer {aiProviderApiKey}");

            var response2 = await httpClient2.PostAsJsonAsync("https://api.ai-video-studio-provider.local/v1/generate-model-video", new
            {
                model_description = modelDescription,
                product_image_url = string.IsNullOrWhiteSpace(productImageUrl) ? null : productImageUrl,
                background_music_track_id = backgroundMusicTrackId
            });

            if (!response2.IsSuccessStatusCode)
                return new AiVideoStoryResult { IsSuccess = false, Message = $"سرویس تولید ویدیوی مدل خطا داد (کد {(int)response2.StatusCode})." };

            var videoPayload = await response2.Content.ReadFromJsonAsync<AiVideoStudioApiResponse>();
            return new AiVideoStoryResult
            {
                IsSuccess = videoPayload?.Success ?? false,
                VideoStoryUrl = videoPayload?.VideoUrl,
                DurationSeconds = videoPayload?.DurationSeconds ?? 0,
                BackgroundMusicTrackId = backgroundMusicTrackId,
                Message = videoPayload?.Success == true ? "ویدیوی مدل با موفقیت تولید شد." : "تولید ویدیوی مدل ناموفق بود."
            };
        }
    }

    public class AiImageStudioResult
    {
        public bool IsSuccess { get; set; }
        public string EnhancedImageUrl { get; set; }
        public bool SkuWatermarkApplied { get; set; }
        public string SkuCode { get; set; }
        public string Message { get; set; }
    }

    public class AiVideoStoryResult
    {
        public bool IsSuccess { get; set; }
        public string VideoStoryUrl { get; set; }
        public int DurationSeconds { get; set; }
        public string BackgroundMusicTrackId { get; set; }
        public string Message { get; set; }
    }

    public class AiVoiceOverResult
    {
        public bool IsSuccess { get; set; }
        public string AudioMp3Url { get; set; }
        public string Speaker { get; set; }
        public string Message { get; set; }
    }

    internal class AiImageStudioApiResponse
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("result_url")] public string ResultUrl { get; set; }
    }

    internal class AiVideoStudioApiResponse
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("video_url")] public string VideoUrl { get; set; }
        [JsonPropertyName("duration_seconds")] public int DurationSeconds { get; set; }
    }

    internal class AiTtsApiResponse
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("audio_url")] public string AudioUrl { get; set; }
    }
}
