namespace Nop.Plugin.Misc.MultiTenantStores.Controllers.Admin
{
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Web.Framework;
    using Nop.Web.Framework.Controllers;
    using Nop.Web.Framework.Mvc.Filters;
    using Nop.Services.Security;
    using Nop.Plugin.Misc.MultiTenantStores.Infrastructure.Filters;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// اکشن‌های مدیریت چک‌لیست «فروشگاه‌ت رو بترکون» — دکمه‌های «انجام دادم / بعداً» در ویجت
    /// داشبورد ادمین. وضعیت هر آیتم به‌ازای همان فروشگاه (تننت) ذخیره می‌شود.
    /// </summary>
    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [AutoValidateAntiforgeryToken]
    [ServiceFilter(typeof(TenantAdminScopeFilter))]
    public class LaunchChecklistController : BasePluginController
    {
        private readonly ILaunchChecklistService _checklistService;
        private readonly Nop.Core.IStoreContext _storeContext;
        private readonly IPermissionService _permissionService;

        public LaunchChecklistController(
            ILaunchChecklistService checklistService,
            Nop.Core.IStoreContext storeContext,
            IPermissionService permissionService)
        {
            _checklistService = checklistService;
            _storeContext = storeContext;
            _permissionService = permissionService;
        }

        /// <summary>دریافت JSON وضعیت چک‌لیست (برای ویجت داشبورد).</summary>
        [HttpGet]
        public async Task<IActionResult> GetItems()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
                return Json(new { success = false, message = "دسترسی رد شد." });

            var store = await _storeContext.GetCurrentStoreAsync();
            var items = await _checklistService.GetChecklistAsync(store.Id);

            return Json(items.Select(i => new
            {
                i.ItemKey,
                i.Title,
                i.Description,
                i.GuideUrl,
                i.IconEmoji,
                i.IsAutoDetected,
                i.AutoStatusLabel,
                status = i.Status.ToString()
            }));
        }

        /// <summary>علامت‌گذاری «انجام دادم» برای یک آیتم دستی.</summary>
        [HttpPost]
        public async Task<IActionResult> MarkDone(string itemKey)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
                return Json(new { success = false, message = "دسترسی رد شد." });

            if (string.IsNullOrWhiteSpace(itemKey))
                return Json(new { success = false, message = "شناسهٔ آیتم نامعتبر است." });

            var store = await _storeContext.GetCurrentStoreAsync();
            await _checklistService.MarkDoneAsync(store.Id, itemKey.Trim());

            return RedirectToAction("Index", "Home", new { area = AreaNames.ADMIN });
        }

        /// <summary>علامت‌گذاری «بعداً» (فعلاً از اولویت اصلی خارج می‌شود).</summary>
        [HttpPost]
        public async Task<IActionResult> MarkLater(string itemKey)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
                return Json(new { success = false, message = "دسترسی رد شد." });

            if (string.IsNullOrWhiteSpace(itemKey))
                return Json(new { success = false, message = "شناسهٔ آیتم نامعتبر است." });

            var store = await _storeContext.GetCurrentStoreAsync();
            await _checklistService.MarkSnoozedAsync(store.Id, itemKey.Trim());

            return RedirectToAction("Index", "Home", new { area = AreaNames.ADMIN });
        }
    }
}
