namespace Nop.Plugin.Api.Controllers.Public
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Core.Domain.Customers;
    using Nop.Services.Common;
    using Nop.Services.Customers;
    using Nop.Services.Stores;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    [ApiController]
    [Route("api/public/deeplink")]
    public class DeepLinkRoutingController : ControllerBase
    {
        private readonly IStoreContext _storeContext;
        private readonly IStoreService _storeService;
        private readonly ICustomerService _customerService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly ITenantProvisioningService _tenantProvisioningService;

        public DeepLinkRoutingController(
            IStoreContext storeContext,
            IStoreService storeService,
            ICustomerService customerService,
            IGenericAttributeService genericAttributeService,
            ITenantProvisioningService tenantProvisioningService)
        {
            _storeContext = storeContext;
            _storeService = storeService;
            _customerService = customerService;
            _genericAttributeService = genericAttributeService;
            _tenantProvisioningService = tenantProvisioningService;
        }

        /// <summary>
        /// دریافت پیکربندی دیپ‌لینک و لینک‌های استور بر اساس شناسه فروشگاه یا دامنه
        /// </summary>
        [HttpGet("store-config/{storeId}")]
        public async Task<IActionResult> GetStoreDeepLinkConfig(int storeId)
        {
            var store = await _storeService.GetStoreByIdAsync(storeId);
            if (store == null)
                return NotFound(new { message = "فروشگاه مورد نظر یافت نشد." });

            var config = new
            {
                StoreId = store.Id,
                StoreName = store.Name,
                StoreUrl = store.Url,
                CustomAppScheme = $"storeapp://store/{store.Id}",
                UniversalLink = $"{store.Url.TrimEnd('/')}/app/join?storeId={store.Id}",
                AndroidPackageName = "com.multitenant.storeapp",
                IosAppStoreId = "id1689001122",
                DirectApkDownloadUrl = $"{store.Url.TrimEnd('/')}/downloads/apps/customer-app.apk",
                EnableDeferredDeepLinking = true
            };

            return Ok(config);
        }

        /// <summary>
        /// استعلام فروشگاه متناظر با شماره موبایل کاربر (برای ورود بدون تایپ دستی در اولین اجرای اپ)
        /// </summary>
        [HttpPost("resolve-store-by-phone")]
        public async Task<IActionResult> ResolveStoreByPhone([FromBody] PhoneStoreResolveRequest dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.PhoneNumber))
                return BadRequest(new { message = "شماره موبایل الزامی است." });

            // ICustomerService متد تک‌نتیجه‌ای «GetCustomerByPhone» ندارد؛ جست‌وجوی واقعی از طریق
            // GetAllCustomersAsync با فیلتر phone انجام می‌شود (شماره موبایل باید یکتا فرض شود).
            var matchingCustomers = await _customerService.GetAllCustomersAsync(phone: dto.PhoneNumber, pageSize: 1);
            var customer = matchingCustomers.FirstOrDefault();

            if (customer == null)
            {
                var currentStore = await _storeContext.GetCurrentStoreAsync();
                return Ok(new
                {
                    IsNewCustomer = true,
                    StoreId = currentStore.Id,
                    StoreName = currentStore.Name
                });
            }

            var homeStoreIdAttr = await _genericAttributeService.GetAttributeAsync<int>(customer, "HomeStoreId");
            var resolvedStoreId = homeStoreIdAttr > 0 ? homeStoreIdAttr : customer.RegisteredInStoreId;

            var targetStore = await _storeService.GetStoreByIdAsync(resolvedStoreId);

            return Ok(new
            {
                IsNewCustomer = false,
                CustomerId = customer.Id,
                StoreId = resolvedStoreId,
                StoreName = targetStore?.Name ?? "فروشگاه اختصاصی"
            });
        }
    }

    public class PhoneStoreResolveRequest
    {
        public string PhoneNumber { get; set; }
        public string ReferralCode { get; set; }
    }
}