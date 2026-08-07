namespace Nop.Plugin.Misc.MultiTenantStores.Infrastructure.Filters
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Filters;
    using Nop.Core;
    using Nop.Services.Common;
    using Nop.Services.Customers;

    /// <summary>
    /// فیلتر عدم ورود مشتریان یک فروشگاه به سایر فروشگاه‌های شبکه
    /// </summary>
    public class CrossStoreCustomerGuardFilter : IAsyncActionFilter
    {
        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly ICustomerService _customerService;
        private readonly IGenericAttributeService _genericAttributeService;

        public CrossStoreCustomerGuardFilter(
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
            var customer = await _workContext.GetCurrentCustomerAsync();
            var currentStore = await _storeContext.GetCurrentStoreAsync();

            if (customer != null && !await _customerService.IsGuestAsync(customer))
            {
                // خواندن HomeStoreId ثبت شده برای کاربر
                var homeStoreId = await _genericAttributeService.GetAttributeAsync<int>(customer, "HomeStoreId");
                var registeredInStoreId = customer.RegisteredInStoreId;

                var boundStoreId = homeStoreId > 0 ? homeStoreId : registeredInStoreId;

                // اگر مشتری وابسته به فروشگاه دیگری باشد و بخواهد روی فروشگاه غیرمرتبط خرید یا لاگین کند
                if (boundStoreId > 0 && boundStoreId != currentStore.Id)
                {
                    // در درخواست‌های API
                    if (context.HttpContext.Request.Path.StartsWithSegments("/api"))
                    {
                        context.Result = new ObjectResult(new
                        {
                            error = "CrossStoreAccessDenied",
                            message = "حساب کاربری شما متعلق به فروشگاه دیگری در شبکه است.",
                            userHomeStoreId = boundStoreId,
                            currentStoreId = currentStore.Id
                        })
                        {
                            StatusCode = 403
                        };
                        return;
                    }
                }
            }

            await next();
        }
    }
}