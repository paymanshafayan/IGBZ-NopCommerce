namespace Nop.Plugin.Misc.MultiTenantStores.Consumers
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Nop.Core.Domain.Customers;
    using Nop.Services.Events;
    using Nop.Plugin.Misc.MultiTenantStores.Infrastructure.Filters;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// در لحظهٔ ثبت‌نام موفق، اگر Cookie معرف (ست‌شده توسط <see cref="ReferralCookieCaptureFilter"/>)
    /// وجود داشت، مشتری جدید را به معرف متصل می‌کند.
    /// </summary>
    public class CustomerReferralRegistrationConsumer : IConsumer<CustomerRegisteredEvent>
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IAffiliateMarketingService _affiliateService;

        public CustomerReferralRegistrationConsumer(
            IHttpContextAccessor httpContextAccessor,
            IAffiliateMarketingService affiliateService)
        {
            _httpContextAccessor = httpContextAccessor;
            _affiliateService = affiliateService;
        }

        public async Task HandleEventAsync(CustomerRegisteredEvent eventMessage)
        {
            var referralCode = _httpContextAccessor.HttpContext?.Request.Cookies[ReferralCookieCaptureFilter.CookieName];
            if (string.IsNullOrWhiteSpace(referralCode))
                return;

            await _affiliateService.CaptureReferralOnRegistrationAsync(eventMessage.Customer.Id, referralCode);
        }
    }
}
