namespace Nop.Plugin.Misc.MultiTenantStores.Controllers.Admin
{
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Web.Framework;
    using Nop.Web.Framework.Controllers;
    using Nop.Web.Framework.Mvc.Filters;
    using Nop.Services.Catalog;
    using Nop.Services.Security;
    using Nop.Data;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;

    /// <summary>
    /// مدیریت سرفصل‌های دوره از پنل ادمین (Product = دوره، طبق راهنمای راه‌اندازی سرویس آموزش).
    /// </summary>
    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [AutoValidateAntiforgeryToken]
    [ServiceFilter(typeof(Infrastructure.Filters.TenantAdminScopeFilter))]
    public class CourseLessonsController : BasePluginController
    {
        private readonly IRepository<CourseLesson> _lessonRepository;
        private readonly IProductService _productService;
        private readonly IPermissionService _permissionService;

        public CourseLessonsController(
            IRepository<CourseLesson> lessonRepository,
            IProductService productService,
            IPermissionService permissionService)
        {
            _lessonRepository = lessonRepository;
            _productService = productService;
            _permissionService = permissionService;
        }

        public async Task<IActionResult> Index(int productId)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
                return AccessDeniedView();

            var product = await _productService.GetProductByIdAsync(productId);
            if (product == null)
                return RedirectToAction("List", "Product", new { area = AreaNames.ADMIN });

            var lessons = await _lessonRepository.GetAllAsync(q =>
                q.Where(l => l.ProductId == productId).OrderBy(l => l.DisplayOrder));

            ViewBag.ProductId = productId;
            ViewBag.ProductName = product.Name;

            return View("~/Plugins/Misc.MultiTenantStores/Views/CourseLessons/Index.cshtml", lessons);
        }

        [HttpPost]
        public async Task<IActionResult> Create(int productId, string title, int durationMinutes, string vodVideoPath, string attachmentUrl, bool isFreePreview)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
                return AccessDeniedView();

            var existingCount = (await _lessonRepository.GetAllAsync(q => q.Where(l => l.ProductId == productId))).Count;

            await _lessonRepository.InsertAsync(new CourseLesson
            {
                ProductId = productId,
                Title = title,
                DisplayOrder = existingCount + 1,
                DurationMinutes = durationMinutes,
                VodVideoPath = vodVideoPath,
                AttachmentUrl = attachmentUrl,
                IsFreePreview = isFreePreview,
                CreatedOnUtc = System.DateTime.UtcNow
            });

            return RedirectToAction("Index", new { productId });
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id, int productId)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
                return AccessDeniedView();

            var lesson = await _lessonRepository.GetByIdAsync(id);
            if (lesson != null)
                await _lessonRepository.DeleteAsync(lesson);

            return RedirectToAction("Index", new { productId });
        }
    }
}
