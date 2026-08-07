namespace Nop.Plugin.Misc.InstagramAssistant.Services
{
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;

    public class AiVisionQualityService : IAiVisionQualityService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public AiVisionQualityService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<ImageQualityCheckResult> ValidateImageQualityAsync(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0)
            {
                return new ImageQualityCheckResult { IsValid = false, Issues = new List<string> { "فایل تصویر خالی است." } };
            }

            var httpClient = _httpClientFactory.CreateClient("AiVisionQuality");
            using var content = new MultipartFormDataContent
            {
                { new ByteArrayContent(imageBytes), "image", "upload.jpg" }
            };

            try
            {
                var response = await httpClient.PostAsync("https://api.ai-vision-provider.local/v1/quality-check", content);
                if (!response.IsSuccessStatusCode)
                {
                    return new ImageQualityCheckResult { IsValid = false, Issues = new List<string> { $"سرویس آنالیز کیفیت خطا داد (کد {(int)response.StatusCode})." } };
                }

                var payload = await response.Content.ReadFromJsonAsync<VisionQualityApiResponse>();
                return new ImageQualityCheckResult
                {
                    IsValid = payload?.IsValid ?? false,
                    Issues = payload?.Issues ?? new List<string>()
                };
            }
            catch (HttpRequestException ex)
            {
                return new ImageQualityCheckResult { IsValid = false, Issues = new List<string> { $"ارتباط با سرویس آنالیز کیفیت برقرار نشد: {ex.Message}" } };
            }
        }

        private class VisionQualityApiResponse
        {
            [JsonPropertyName("is_valid")] public bool IsValid { get; set; }
            [JsonPropertyName("issues")] public List<string> Issues { get; set; }
        }
    }

    public class AiBackgroundRemovalService : IAiBackgroundRemovalService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public AiBackgroundRemovalService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<byte[]> RemoveBackgroundAsync(byte[] imageBytes)
        {
            var httpClient = _httpClientFactory.CreateClient("AiBackgroundRemoval");
            using var content = new MultipartFormDataContent
            {
                { new ByteArrayContent(imageBytes), "image", "upload.jpg" }
            };

            var response = await httpClient.PostAsync("https://api.ai-bg-removal-provider.local/v1/remove-background", content);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }
    }
}
