namespace Nop.Plugin.Misc.MultiTenantStores.Controllers.Admin
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Web.Framework;
    using Nop.Web.Framework.Controllers;
    using Nop.Web.Framework.Mvc.Filters;
    using Nop.Services.Security;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// پنل ادمین برای بررسی و تسویهٔ درخواست‌های برداشت معرف‌ها (طبق راهنمای Affiliate Marketing،
    /// بند «طراحی داشبورد ادمین»).
    /// </summary>
    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [AutoValidateAntiforgeryToken]
    [ServiceFilter(typeof(Infrastructure.Filters.TenantAdminScopeFilter))]
    public class AffiliateWithdrawalRequestsController : BasePluginController
    {
        private readonly IStoreContext _storeContext;
        private readonly IPermissionService _permissionService;
        private readonly IAffiliateMarketingService _affiliateService;

        public AffiliateWithdrawalRequestsController(
            IStoreContext storeContext,
            IPermissionService permissionService,
            IAffiliateMarketingService affiliateService)
        {
            _storeContext = storeContext;
            _permissionService = permissionService;
            _affiliateService = affiliateService;
        }

        public async Task<IActionResult> Index()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
                return AccessDeniedView();

            var currentStore = await _storeContext.GetCurrentStoreAsync();
            var pending = await _affiliateService.GetPendingWithdrawalRequestsAsync(currentStore.Id);

            return View("~/Plugins/Misc.MultiTenantStores/Views/AffiliateWithdrawalRequests/Index.cshtml", pending);
        }

        [HttpPost]
        public async Task<IActionResult> Approve(int id, string note)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
                return AccessDeniedView();

            var approved = await _affiliateService.ApproveWithdrawalAsync(id, note);
            if (!approved)
                ErrorNotification("موجودی کیف‌پول کاربر از زمان درخواست کاهش یافته و دیگر برای این برداشت کافی نیست.");

            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> Reject(int id, string note)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
                return AccessDeniedView();

            await _affiliateService.RejectWithdrawalAsync(id, note);
            return RedirectToAction("Index");
        }
    }
}
