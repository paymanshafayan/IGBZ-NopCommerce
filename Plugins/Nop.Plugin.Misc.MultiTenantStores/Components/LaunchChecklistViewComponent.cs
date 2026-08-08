namespace Nop.Plugin.Misc.MultiTenantStores.Components
{
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.ViewComponents;
    using Nop.Core;
    using Nop.Web.Framework.Components;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// ویجت «فروشگاه‌ت رو بترکون» — چک‌لیست راه‌اندازی/رشد فروشگاه که فقط در صفحهٔ نخست
    /// (داشبورد) پنل ادمین نمایش داده می‌شود. از زون <c>admin_content_before</c> تزریق می‌شود.
    /// </summary>
    public class LaunchChecklistViewComponent : NopViewComponent
    {
        private readonly ILaunchChecklistService _checklistService;
        private readonly IStoreContext _storeContext;

        public LaunchChecklistViewComponent(
            ILaunchChecklistService checklistService,
            IStoreContext storeContext)
        {
            _checklistService = checklistService;
            _storeContext = storeContext;
        }

        public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
        {
            // فقط روی داشبورد ادمین (Home/Index در Area=Admin) نمایش داده شود، نه همهٔ صفحات ادمین
            var routeValues = ViewComponentContext.HttpContext.Request.RouteValues;
            var isAdminDashboard =
                string.Equals(routeValues["area"]?.ToString(), "Admin", System.StringComparison.OrdinalIgnoreCase)
                && string.Equals(routeValues["controller"]?.ToString(), "Home", System.StringComparison.OrdinalIgnoreCase)
                && string.Equals(routeValues["action"]?.ToString(), "Index", System.StringComparison.OrdinalIgnoreCase);

            if (!isAdminDashboard)
                return Content(string.Empty);

            var store = await _storeContext.GetCurrentStoreAsync();
            var items = await _checklistService.GetChecklistAsync(store.Id);
            var pendingCount = items.Count(i => i.Status == Domain.LaunchChecklistStatus.Pending);

            var model = new LaunchChecklistModel
            {
                StoreId = store.Id,
                PendingCount = pendingCount,
                Items = items
            };

            return View("~/Plugins/Misc.MultiTenantStores/Views/Shared/Components/LaunchChecklist/Default.cshtml", model);
        }
    }

    public class LaunchChecklistModel
    {
        public int StoreId { get; set; }
        public int PendingCount { get; set; }
        public System.Collections.Generic.IList<LaunchChecklistItemDto> Items { get; set; }
    }
}
