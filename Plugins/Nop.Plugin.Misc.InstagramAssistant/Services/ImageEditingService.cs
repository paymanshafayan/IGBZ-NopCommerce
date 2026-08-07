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
    /// ابزارهای واقعی ویرایش دستی خروجی AI (برش، پاک‌کن، جابجایی متن/قالب) — طبق درخواست صریح کاربر
    /// برای «عمق کامل». نکتهٔ معماری مهم: بخش *تعاملی* (کشیدن قلم‌مو روی صفحه، درگ‌کردن متن با
    /// انگشت) لزوماً باید سمت کلاینت (اپ فلاتر، با یک Canvas واقعی) اتفاق بیفتد — سرور نمی‌تواند
    /// جای انگشت کاربر روی صفحه را حدس بزند. نقش این سرویس این است: کلاینت نتیجهٔ تعامل کاربر را
    /// به‌صورت داده‌های ساختاریافته (مختصات کراپ، تصویر ماسک پاک‌کن، مختصات نهایی متن) به سرور
    /// می‌فرستد، و سرور آن را به‌صورت قابل‌اتکا و امن (نه در حافظهٔ موقت کلاینت) روی تصویر واقعی
    /// اعمال و ذخیره می‌کند.
    /// </summary>
    public interface IImageEditingService
    {
        Task<byte[]> CropAsync(byte[] imageBytes, int x, int y, int width, int height);

        /// <summary>
        /// اعمال «پاک‌کن»: هرجا تصویر ماسک روشن/سفید باشد، همان ناحیه از تصویر اصلی شفاف
        /// (Transparent) می‌شود. تصویر ماسک را کلاینت از روی مسیر قلم‌موی کاربر رسم و ارسال می‌کند.
        /// </summary>
        Task<byte[]> ApplyEraserMaskAsync(byte[] imageBytes, byte[] maskBytes);

        /// <summary>افزودن متن دلخواه کاربر در مختصات دقیقی که خودش (با درگ‌کردن در اپ) انتخاب کرده.</summary>
        Task<byte[]> AddTextAsync(byte[] imageBytes, string text, int x, int y, int fontSize, string hexColor);

        /// <summary>مونتاژ یک قالب/فریم آماده (PNG با شفافیت) روی تصویر، در مختصات انتخابی کاربر.</summary>
        Task<byte[]> AddTemplateOverlayAsync(byte[] imageBytes, byte[] templateOverlayBytes, int x, int y);
    }

    public class ImageEditingService : IImageEditingService
    {
        public async Task<byte[]> CropAsync(byte[] imageBytes, int x, int y, int width, int height)
        {
            using var image = Image.Load(imageBytes);

            // مختصات درخواستی را به محدودهٔ واقعی تصویر محدود می‌کنیم تا کاربر با فرستادن مقادیر
            // اشتباه (مثلاً پهنای بیشتر از خودِ تصویر) باعث Exception روی سرور نشود.
            var safeX = Math.Clamp(x, 0, image.Width - 1);
            var safeY = Math.Clamp(y, 0, image.Height - 1);
            var safeWidth = Math.Clamp(width, 1, image.Width - safeX);
            var safeHeight = Math.Clamp(height, 1, image.Height - safeY);

            image.Mutate(ctx => ctx.Crop(new Rectangle(safeX, safeY, safeWidth, safeHeight)));

            using var ms = new MemoryStream();
            await image.SaveAsPngAsync(ms);
            return ms.ToArray();
        }

        public async Task<byte[]> ApplyEraserMaskAsync(byte[] imageBytes, byte[] maskBytes)
        {
            using var image = Image.Load<Rgba32>(imageBytes);
            using var mask = Image.Load<Rgba32>(maskBytes);

            // ماسک ممکن است اندازه‌اش با تصویر اصلی فرق کند (مثلاً کلاینت آن را در رزولوشن نمایش
            // صفحه رسم کرده) — قبل از اعمال، دقیقاً به سایز تصویر اصلی Resize می‌شود.
            if (mask.Width != image.Width || mask.Height != image.Height)
                mask.Mutate(ctx => ctx.Resize(image.Width, image.Height));

            // ⚠️ ProcessPixelRows با دو تصویر هم‌زمان یک API نسبتاً کم‌استفاده‌تر ImageSharp است؛
            // در صورت خطای Build روی این خط، جایگزین امن‌تر پیمایش دستی با اندیسر image[x,y] است
            // (کندتر ولی مطمئناً در همهٔ نسخه‌ها کار می‌کند).
            image.ProcessPixelRows(mask, (imageAccessor, maskAccessor) =>
            {
                for (var rowIndex = 0; rowIndex < imageAccessor.Height; rowIndex++)
                {
                    var imageRow = imageAccessor.GetRowSpan(rowIndex);
                    var maskRow = maskAccessor.GetRowSpan(rowIndex);

                    for (var columnIndex = 0; columnIndex < imageRow.Length; columnIndex++)
                    {
                        // روشنایی ماسک را به‌عنوان میزان «پاک شدن» در نظر می‌گیریم: سفید کامل = کاملاً شفاف.
                        var maskPixel = maskRow[columnIndex];
                        var eraseStrength = (maskPixel.R + maskPixel.G + maskPixel.B) / (3f * 255f);

                        ref var pixel = ref imageRow[columnIndex];
                        pixel.A = (byte)(pixel.A * (1f - eraseStrength));
                    }
                }
            });

            using var ms = new MemoryStream();
            await image.SaveAsPngAsync(ms); // PNG لازم است تا کانال شفافیت (Alpha) حفظ شود
            return ms.ToArray();
        }

        public async Task<byte[]> AddTextAsync(byte[] imageBytes, string text, int x, int y, int fontSize, string hexColor)
        {
            using var image = Image.Load(imageBytes);

            var font = SystemFonts.CreateFont("Arial", fontSize <= 0 ? 24 : fontSize, FontStyle.Bold);
            var color = ParseHexColorOrDefault(hexColor, Color.White);

            image.Mutate(ctx => ctx.DrawText(text ?? string.Empty, font, color, new PointF(x, y)));

            using var ms = new MemoryStream();
            await image.SaveAsPngAsync(ms);
            return ms.ToArray();
        }

        public async Task<byte[]> AddTemplateOverlayAsync(byte[] imageBytes, byte[] templateOverlayBytes, int x, int y)
        {
            using var image = Image.Load(imageBytes);
            using var overlay = Image.Load(templateOverlayBytes);

            image.Mutate(ctx => ctx.DrawImage(overlay, new Point(x, y), 1f));

            using var ms = new MemoryStream();
            await image.SaveAsPngAsync(ms);
            return ms.ToArray();
        }

        private static Color ParseHexColorOrDefault(string hexColor, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hexColor))
                return fallback;

            try
            {
                return Color.ParseHex(hexColor);
            }
            catch (ArgumentException)
            {
                return fallback;
            }
        }
    }
}
