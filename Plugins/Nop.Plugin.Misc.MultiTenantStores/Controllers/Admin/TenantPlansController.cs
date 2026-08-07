namespace Nop.Plugin.Misc.MultiTenantStores.Controllers.Admin
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Services.Customers;
    using Nop.Services.Common;
    using Nop.Web.Framework;
    using Nop.Web.Framework.Controllers;
    using Nop.Web.Framework.Mvc.Filters;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// مدیریت پلن‌های اشتراکی (برنزی/نقره‌ای/طلایی/آزمایشی) — کاملاً قابل درج/ویرایش/حذف از پنل
    /// مدیریت، طبق درخواست صریح کاربر. چون این داده سطح کل پلتفرم است (نه مخصوص یک فروشگاه)، فقط
    /// سوپرادمین (نه ادمین تننت) به آن دسترسی دارد — به همین دلیل عمداً TenantAdminScopeFilter روی
    /// این کنترلر اعمال نشده.
    /// </summary>
    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [AutoValidateAntiforgeryToken]
    public class TenantPlansController : BasePluginController
    {
        private readonly IWorkContext _workContext;
        private readonly ICustomerService _customerService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly ITenantPlanService _planService;

        public TenantPlansController(
            IWorkContext workContext,
            ICustomerService customerService,
            IGenericAttributeService genericAttributeService,
            ITenantPlanService planService)
        {
            _workContext = workContext;
            _customerService = customerService;
            _genericAttributeService = genericAttributeService;
            _planService = planService;
        }

        private async Task<bool> IsSuperAdminAsync()
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            if (customer == null) return false;
            var isTenantOwner = await _genericAttributeService.GetAttributeAsync<bool>(customer, "IsTenantOwner");
            return await _customerService.IsAdminAsync(customer) && !isTenantOwner;
        }

        public async Task<IActionResult> Index()
        {
            if (!await IsSuperAdminAsync())
                return AccessDeniedView();

            var plans = await _planService.GetAllPlansAsync();
            return View("~/Plugins/Misc.MultiTenantStores/Views/TenantPlans/Index.cshtml", plans);
        }

        public async Task<IActionResult> Create()
        {
            if (!await IsSuperAdminAsync())
                return AccessDeniedView();

            return View("~/Plugins/Misc.MultiTenantStores/Views/TenantPlans/CreateOrUpdate.cshtml", new TenantPlan { IsActive = true, AllowStore = true });
        }

        [HttpPost]
        public async Task<IActionResult> Create(TenantPlan model)
        {
            if (!await IsSuperAdminAsync())
                return AccessDeniedView();

            await _planService.InsertPlanAsync(model);
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> Edit(int id)
        {
            if (!await IsSuperAdminAsync())
                return AccessDeniedView();

            var plan = await _planService.GetPlanByIdAsync(id);
            if (plan == null)
                return RedirectToAction("Index");

            return View("~/Plugins/Misc.MultiTenantStores/Views/TenantPlans/CreateOrUpdate.cshtml", plan);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(TenantPlan model)
        {
            if (!await IsSuperAdminAsync())
                return AccessDeniedView();

            var plan = await _planService.GetPlanByIdAsync(model.Id);
            if (plan == null)
                return RedirectToAction("Index");

            plan.Name = model.Name;
            plan.SystemName = model.SystemName;
            plan.Description = model.Description;
            plan.LinkedProductId = model.LinkedProductId;
            plan.MaxProductsAllowed = model.MaxProductsAllowed;
            plan.MaxOrdersPerMonth = model.MaxOrdersPerMonth;
            plan.AllowCustomDomain = model.AllowCustomDomain;
            plan.AllowDedicatedApp = model.AllowDedicatedApp;
            plan.AllowStore = model.AllowStore;
            plan.AllowInstagramAiAssistant = model.AllowInstagramAiAssistant;
            // طبق قاعدهٔ کسب‌وکاری: Pro شامل عادی است — اگر Pro فعال شد، عادی هم باید فعال باشد.
            plan.AllowInstagramAiAssistantPro = model.AllowInstagramAiAssistantPro;
            if (plan.AllowInstagramAiAssistantPro)
                plan.AllowInstagramAiAssistant = true;
            plan.PriceMonthly = model.PriceMonthly;
            plan.PriceSixMonths = model.PriceSixMonths;
            plan.PriceYearly = model.PriceYearly;
            plan.TrialDurationDays = model.TrialDurationDays;
            plan.DisplayOrder = model.DisplayOrder;
            plan.IsActive = model.IsActive;

            await _planService.UpdatePlanAsync(plan);
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            if (!await IsSuperAdminAsync())
                return AccessDeniedView();

            await _planService.DeletePlanAsync(id);
            return RedirectToAction("Index");
        }
    }
}
