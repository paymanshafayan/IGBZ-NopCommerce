namespace Nop.Plugin.Api.Controllers.Admin
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Core.Domain.Media;
    using Nop.Services.Catalog;
    using Nop.Services.Media;

    [ApiController]
    [Route("api/admin/digital-products")]
    public class AdminProductDownloadsController : AuthorizedTenantOwnerApiController
    {
        private readonly IProductService _productService;
        private readonly IDownloadService _downloadService;

        public AdminProductDownloadsController(
            IWorkContext workContext,
            IStoreContext storeContext,
            IProductService productService,
            IDownloadService downloadService) : base(workContext, storeContext)
        {
            _productService = productService;
            _downloadService = downloadService;
        }

        [HttpPost("upload/{productId}")]
        public async Task<IActionResult> UploadDigitalFile(int productId, IFormFile file, [FromForm] int maxDownloads = 10, [FromForm] int expirationDays = 30)
        {
            var store = await GetAuthorizedStoreAsync();
            var product = await _productService.GetProductByIdAsync(productId);

            if (product == null)
                return NotFound(new { message = "محصول مورد نظر یافت نشد." });

            if (file == null || file.Length == 0)
                return BadRequest(new { message = "فایل ارسالی خالی است." });

            byte[] fileData;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                fileData = ms.ToArray();
            }

            var download = new Download
            {
                DownloadGuid = Guid.NewGuid(),
                UseDownloadUrl = false,
                DownloadUrl = string.Empty,
                DownloadBinary = fileData,
                ContentType = file.ContentType,
                Filename = Path.GetFileNameWithoutExtension(file.FileName),
                Extension = Path.GetExtension(file.FileName),
                IsNew = true
            };

            await _downloadService.InsertDownloadAsync(download);

            product.IsDownload = true;
            product.DownloadId = download.Id;
            product.MaxNumberOfDownloads = maxDownloads;
            product.DownloadExpirationDays = expirationDays;
            product.HasUserAgreement = true;
            product.UserAgreementText = "این فایل لایسنس تک کاربره داشته و هرگونه انتشار غیرقانونی پیگرد قانونی دارد.";

            await _productService.UpdateProductAsync(product);

            return Ok(new
            {
                success = true,
                productId = product.Id,
                downloadId = download.Id,
                downloadGuid = download.DownloadGuid,
                fileName = file.FileName,
                fileSizeMb = Math.Round((double)file.Length / (1024 * 1024), 2)
            });
        }

        [HttpGet("status/{productId}")]
        public async Task<IActionResult> GetDigitalProductStatus(int productId)
        {
            var product = await _productService.GetProductByIdAsync(productId);
            if (product == null) return NotFound("محصول یافت نشد.");

            if (!product.IsDownload || product.DownloadId == 0)
            {
                return Ok(new { isDigital = false });
            }

            var download = await _downloadService.GetDownloadByIdAsync(product.DownloadId);

            return Ok(new
            {
                isDigital = true,
                downloadId = download?.Id ?? 0,
                downloadGuid = download?.DownloadGuid,
                fileName = download != null ? $"{download.Filename}{download.Extension}" : "فایل ناشناخته",
                maxDownloads = product.MaxNumberOfDownloads,
                expirationDays = product.DownloadExpirationDays
            });
        }
    }
}