namespace Nop.Plugin.Misc.InstagramAssistant.Services
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    public interface IProductPhotoAiStudioService
    {
        Task<PhotoStudioResultDto> ProcessProductPhotoAsync(ProductPhotoRequestDto request);

        /// <summary>فقط درج واترمارک کد محصول — عملیات محلی و رایگان، بدون فراخوانی AI بیرونی.</summary>
        Task<byte[]> ApplyDynamicSkuWatermarkAsync(byte[] imageBytes, string productSku);
    }

    public class ProductPhotoRequestDto
    {
        public string ProductSku { get; set; }
        public byte[] RawImageBytes { get; set; }
        public string BackgroundPreset { get; set; }
    }

    public class PhotoStudioResultDto
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public IList<string> DiagnosticErrors { get; set; }
        public byte[] ProcessedImageBytes { get; set; }
        public string ProductSku { get; set; }
    }

    /// <summary>
    /// سرویس آنالیز کیفی تصویر پیش از پردازش تجاری (وضوح، نور، فوکوس و...). پیاده‌سازی واقعی
    /// باید به یک سرویس Vision AI بیرونی (یا مدل محلی) متصل شود؛ هرگز نباید همیشه true برگرداند.
    /// </summary>
    public interface IAiVisionQualityService
    {
        Task<ImageQualityCheckResult> ValidateImageQualityAsync(byte[] imageBytes);
    }

    public class ImageQualityCheckResult
    {
        public bool IsValid { get; set; }
        public IList<string> Issues { get; set; } = new List<string>();
    }

    /// <summary>
    /// سرویس حذف پس‌زمینه تصویر از طریق یک API بیرونی واقعی.
    /// </summary>
    public interface IAiBackgroundRemovalService
    {
        Task<byte[]> RemoveBackgroundAsync(byte[] imageBytes);
    }
}
