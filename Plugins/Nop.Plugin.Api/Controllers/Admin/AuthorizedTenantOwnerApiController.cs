namespace Nop.Plugin.Api.Controllers.Admin
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Core.Domain.Customers;
    using Nop.Core.Domain.Stores;

    /// <summary>
    /// کلاس پایه مشترک برای تمام کنترلرهای Admin API که فقط باید توسط «مالک تننت» یا مدیر ارشد
    /// سیستم فراخوانی شوند. این کلاس قبلاً در پروژه رفرنس داده می‌شد اما هرگز تعریف نشده بود —
    /// در نتیجه کل لایه Admin API کامپایل نمی‌شد. منطق مجازشماری این کلاس با
    /// <c>TenantAdminScopeFilter</c> (پلاگین MultiTenantStores) هم‌راستا است: هر ادمین فقط به
    /// فروشگاه (Store) خودش دسترسی دارد، مگر آن‌که Super Admin سیستم باشد.
    /// </summary>
    [ApiController]
    public abstract class AuthorizedTenantOwnerApiController : ControllerBase
    {
        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;

        protected AuthorizedTenantOwnerApiController(IWorkContext workContext, IStoreContext storeContext)
        {
            _workContext = workContext;
            _storeContext = storeContext;
        }

        /// <summary>
        /// مشتری/ادمین جاری براساس هدر Authorization درخواست (JWT قبلاً توسط Middleware احراز هویت پردازش شده است).
        /// </summary>
        protected async Task<Customer> GetCurrentCustomerAsync()
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            if (customer == null)
                throw new UnauthorizedAccessException("کاربر احراز هویت نشده است.");

            return customer;
        }

        /// <summary>
        /// فروشگاهی که کاربر جاری مجاز به مدیریت آن است. اگر کاربر مالک تننت باشد و بخواهد فروشگاه
        /// دیگری غیر از فروشگاه خودش را دستکاری کند، دسترسی رد می‌شود (سازگار با TenantAdminScopeFilter).
        /// </summary>
        protected async Task<Store> GetAuthorizedStoreAsync()
        {
            var customer = await GetCurrentCustomerAsync();
            var currentStore = await _storeContext.GetCurrentStoreAsync();

            // در این پروژه مرجع، منطق کامل تشخیص IsTenantOwner/HomeStoreId در سرویس‌های
            // Nop.Plugin.Misc.MultiTenantStores (ICustomerService attribute lookups) پیاده شده است؛
            // این متد صرفاً فروشگاه فعلی تشخیص‌داده‌شده توسط IStoreContext را برمی‌گرداند، چون
            // فیلتر TenantAdminScopeFilter پیش از رسیدن درخواست به اکشن، دسترسی متقاطع را رد کرده است.
            if (currentStore == null)
                throw new UnauthorizedAccessException("فروشگاه معتبری برای این درخواست تشخیص داده نشد.");

            return currentStore;
        }
    }
}
