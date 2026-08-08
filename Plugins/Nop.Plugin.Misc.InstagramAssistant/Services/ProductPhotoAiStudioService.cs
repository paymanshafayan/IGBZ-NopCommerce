namespace Nop.Plugin.Misc.InstagramAssistant.Services
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using SixLabors.Fonts;
    using SixLabors.ImageSharp;
    using SixLabors.ImageSharp.Drawing.Processing;
    using SixLabors.ImageSharp.PixelFormats;
    using SixLabors.ImageSharp.Processing;

    /// <summary>
    /// AI Product Photo Studio & Commercial Image Processor Service (.NET 9)
    /// نیازمند بسته NuGet: SixLabors.ImageSharp و SixLabors.ImageSharp.Drawing
    /// </summary>
    public class ProductPhotoAiStudioService : IProductPhotoAiStudioService
    {
        private readonly IAiVisionQualityService _visionQualityService;
        private readonly IAiBackgroundRemovalService _bgRemovalService;

        public ProductPhotoAiStudioService(
            IAiVisionQualityService visionQualityService,
            IAiBackgroundRemovalService bgRemovalService)
        {
            _visionQualityService = visionQualityService;
            _bgRemovalService = bgRemovalService;
        }

        /// <summary>
        /// پردازش کامل تصویر شامل آنالیز کیفیت (قانون ۱)، ادیت استودیویی (قانون ۲) و درج کد محصول در گوشه پایین چپ (قانون ۳)
        /// </summary>
        public async Task<PhotoStudioResultDto> ProcessProductPhotoAsync(ProductPhotoRequestDto request)
        {
            // ۱. سنجش کیفی تصویر (قانون ۱)
            var qualityCheck = await _visionQualityService.ValidateImageQualityAsync(request.RawImageBytes);
            if (!qualityCheck.IsValid)
            {
                return new PhotoStudioResultDto
                {
                    Success = false,
                    ErrorMessage = "تصویر ورودی کیفیت لازم برای پردازش تجاری را ندارد.",
                    DiagnosticErrors = qualityCheck.Issues
                };
            }

            // ۲. حذف پس‌زمینه با هوش مصنوعی و اعمال نورپردازی استودیویی
            byte[] foregroundImage = await _bgRemovalService.RemoveBackgroundAsync(request.RawImageBytes);

            // ۳. درج کد محصول در گوشه پایین سمت چپ
            var watermarkedBytes = await ApplyDynamicSkuWatermarkAsync(foregroundImage, request.ProductSku);

            return new PhotoStudioResultDto
            {
                Success = true,
                ProcessedImageBytes = watermarkedBytes,
                ProductSku = request.ProductSku
            };
        }

        /// <summary>
        /// فقط درج واترمارک کد محصول (بدون حذف پس‌زمینه، بدون فراخوانی هزینه‌بر AI بیرونی) — عملیاتی
        /// کاملاً محلی و رایگان با SixLabors.ImageSharp. این متد جدا شد تا جاهایی مثل انتشار خودکار
        /// پست اینستاگرام هنگام درج محصول («قانون #۳» کاربر: کد محصول باید در گوشهٔ تصویر پست باشد)
        /// بتوانند فقط واترمارک بزنند، بدون این‌که هزینهٔ حذف پس‌زمینه از کیف‌پول فروشنده کسر شود.
        /// </summary>
        public async Task<byte[]> ApplyDynamicSkuWatermarkAsync(byte[] imageBytes, string productSku)
        {
            using var image = Image.Load(imageBytes);

            var font = SystemFonts.CreateFont("Arial", 22, FontStyle.Bold);
            var watermarkText = $"کد محصول: {productSku}";

            // اندازه‌گیری واقعی متن تا مستطیل پس‌زمینه برای SKUهای بلند هم متناسب باشد
            // (نسخهٔ قبلی عرض ثابت ۲۲۰px داشت و برای SKUهای بلند متن سرریز می‌کرد).
            var textBounds = TextMeasurer.MeasureBounds(watermarkText, new TextOptions(font));
            float padding = 12f;

            var maxRectWidth = Math.Max(1f, image.Width - 4f);
            var rectWidth = Math.Clamp(textBounds.Width + padding * 2, 1f, maxRectWidth);
            var rectHeight = Math.Max(1f, textBounds.Height + padding);

            float x = 12f;
            float y = Math.Max(0f, image.Height - rectHeight - 12f);

            image.Mutate(ctx =>
            {
                ctx.Fill(Color.FromRgba(10, 15, 26, 220), new RectangleF(x, y, rectWidth, rectHeight));
                ctx.DrawText(watermarkText, font, Color.White, new PointF(x + padding, y + padding / 2));
            });

            using var ms = new MemoryStream();
            await image.SaveAsJpegAsync(ms);
            return ms.ToArray();
        }
    }
}