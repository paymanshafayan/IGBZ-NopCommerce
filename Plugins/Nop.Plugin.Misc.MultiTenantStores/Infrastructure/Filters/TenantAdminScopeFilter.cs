namespace Nop.Plugin.Misc.MultiTenantStores.Infrastructure.Filters
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Filters;
    using Nop.Core;
    using Nop.Services.Common;
    using Nop.Services.Customers;

    /// <summary>
    /// فیلتر امنیتی اجبار محدوده فروشگاه برای ادمین هر تننت
    /// </summary>
    public class TenantAdminScopeFilter : IAsyncActionFilter
    {
        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly ICustomerService _customerService;
        private readonly IGenericAttributeService _genericAttributeService;

        public TenantAdminScopeFilter(
            IWorkContext workContext,
            IStoreContext storeContext,
            ICustomerService customerService,
            IGenericAttributeService genericAttributeService)
        {
            _workContext = workContext;
            _storeContext = storeContext;
            _customerService = customerService;
            _genericAttributeService = genericAttributeService;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var currentCustomer = await _workContext.GetCurrentCustomerAsync();
            var currentStore = await _storeContext.GetCurrentStoreAsync();

            if (currentCustomer != null)
            {
                // بررسی اینکه آیا کاربر مالک یک فروشگاه اختصاصی است یا خیر
                var isTenantOwner = await _genericAttributeService.GetAttributeAsync<bool>(currentCustomer, "IsTenantOwner");
                var homeStoreId = await _genericAttributeService.GetAttributeAsync<int>(currentCustomer, "HomeStoreId");

                // اگر ادمین اصلی سیستم (Super Admin) باشد، محدودیتی ندارد
                var isSuperAdmin = await _customerService.IsAdminAsync(currentCustomer) && !isTenantOwner;

                if (isTenantOwner && !isSuperAdmin)
                {
                    // ادمین تننت فقط مجاز است از دامنه/زیردامنه فروشگاه خودش وارد پنل ادمین شود
                    if (homeStoreId > 0 && homeStoreId != currentStore.Id)
                    {
                        context.Result = new ForbidResult("شما تنها مجاز به مدیریت اطلاعات در دامنه اختصاصی فروشگاه خود هستید.");
                        return;
                    }

                    // قفل کردن پارامتر storeId در اکشن‌های ادمین
                    if (context.ActionArguments.ContainsKey("storeId"))
                    {
                        context.ActionArguments["storeId"] = currentStore.Id;
                    }
                }
            }

            await next();
        }
    }
}