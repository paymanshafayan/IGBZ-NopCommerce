namespace Nop.Plugin.Misc.InstagramAssistant.Controllers
{
    using System.IO;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Services.Media;
    using Nop.Plugin.Misc.InstagramAssistant.Services;

    /// <summary>
    /// ابزارهای ویرایش دستی خروجی AI Studio — برش، پاک‌کن، متن، قالب. هر endpoint یک فایل تصویر
    /// می‌گیرد، ویرایش را واقعاً روی آن اعمال می‌کند، و نتیجه را به‌عنوان یک Picture جدید ذخیره
    /// می‌کند تا آدرس عمومی مستقل خودش را داشته باشد (قابل زنجیره‌کردن با ویرایش بعدی، یا اتصال
    /// نهایی به محصول/پست اینستاگرام).
    /// </summary>
    [ApiController]
    [Route("api/instagram/ai-studio/edit")]
    public class ImageEditingController : ControllerBase
    {
        private readonly IImageEditingService _editingService;
        private readonly IPictureService _pictureService;

        public ImageEditingController(IImageEditingService editingService, IPictureService pictureService)
        {
            _editingService = editingService;
            _pictureService = pictureService;
        }

        [HttpPost("crop")]
        public async Task<IActionResult> Crop(IFormFile image, int x, int y, int width, int height)
        {
            var sourceBytes = await ReadFileBytesAsync(image);
            if (sourceBytes == null)
                return BadRequest(new { success = false, message = "فایل تصویر ارسال نشده است." });

            var resultBytes = await _editingService.CropAsync(sourceBytes, x, y, width, height);
            return await SaveAndReturnUrlAsync(resultBytes, "crop");
        }

        /// <summary>ماسک باید یک تصویر سیاه/سفید باشد: نواحی سفید = پاک شود، سیاه = دست‌نخورده بماند.</summary>
        [HttpPost("erase")]
        public async Task<IActionResult> Erase(IFormFile image, IFormFile mask)
        {
            var sourceBytes = await ReadFileBytesAsync(image);
            var maskBytes = await ReadFileBytesAsync(mask);
            if (sourceBytes == null || maskBytes == null)
                return BadRequest(new { success = false, message = "هم تصویر اصلی و هم تصویر ماسک الزامی است." });

            var resultBytes = await _editingService.ApplyEraserMaskAsync(sourceBytes, maskBytes);
            return await SaveAndReturnUrlAsync(resultBytes, "erase");
        }

        [HttpPost("add-text")]
        public async Task<IActionResult> AddText(IFormFile image, string text, int x, int y, int fontSize = 24, string hexColor = "#FFFFFF")
        {
            var sourceBytes = await ReadFileBytesAsync(image);
            if (sourceBytes == null)
                return BadRequest(new { success = false, message = "فایل تصویر ارسال نشده است." });

            var resultBytes = await _editingService.AddTextAsync(sourceBytes, text, x, y, fontSize, hexColor);
            return await SaveAndReturnUrlAsync(resultBytes, "text");
        }

        [HttpPost("add-template")]
        public async Task<IActionResult> AddTemplate(IFormFile image, IFormFile template, int x, int y)
        {
            var sourceBytes = await ReadFileBytesAsync(image);
            var templateBytes = await ReadFileBytesAsync(template);
            if (sourceBytes == null || templateBytes == null)
                return BadRequest(new { success = false, message = "هم تصویر اصلی و هم فایل قالب الزامی است." });

            var resultBytes = await _editingService.AddTemplateOverlayAsync(sourceBytes, templateBytes, x, y);
            return await SaveAndReturnUrlAsync(resultBytes, "template");
        }

        private static async Task<byte[]> ReadFileBytesAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return null;

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            return ms.ToArray();
        }

        private async Task<IActionResult> SaveAndReturnUrlAsync(byte[] resultBytes, string editKind)
        {
            var savedPicture = await _pictureService.InsertPictureAsync(resultBytes, "image/png", $"ai-studio-edit-{editKind}");
            var (url, _) = await _pictureService.GetPictureUrlAsync(savedPicture);

            return Ok(new { success = true, imageUrl = url });
        }
    }
}
