namespace Nop.Plugin.Misc.MultiTenantStores.Infrastructure.Filters
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc.Filters;

    /// <summary>
    /// اگر کاربر با لینک حاوی ?ref=CODE وارد سایت شود، کد معرف را در یک Cookie با عمر ۳۰ روزه
    /// ذخیره می‌کند تا در لحظهٔ ثبت‌نام قابل بازیابی باشد (طبق راهنمای Affiliate Marketing، بند
    /// «استفاده از Action Filters»).
    /// </summary>
    public class ReferralCookieCaptureFilter : IAsyncActionFilter
    {
        public const string CookieName = "igbz_ref";

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var refCode = context.HttpContext.Request.Query["ref"].ToString();
            if (!string.IsNullOrWhiteSpace(refCode))
            {
                context.HttpContext.Response.Cookies.Append(CookieName, refCode.Trim().ToUpperInvariant(), new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddDays(30),
                    HttpOnly = true,
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax
                });
            }

            await next();
        }
    }
}
