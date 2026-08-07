namespace Nop.Plugin.Misc.MultiTenantStores.Services
{
    using System;
    using System.Threading.Tasks;
    using Nop.Core.Domain.Customers;
    using Nop.Core.Domain.Stores;
    using Nop.Services.Customers;
    using Nop.Services.Common;
    using Nop.Services.Stores;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;

    public interface ITenantProvisioningService
    {
        Task<bool> ValidateSubdomainAvailabilityAsync(string subdomain);
        Task<ProvisioningResult> ProvisionNewTenantStoreAsync(ProvisionTenantRequest request);
        Task ActivateTenantStoreAsync(int storeId);
        Task SuspendTenantStoreAsync(int storeId, string reason);
    }

    public class TenantProvisioningService : ITenantProvisioningService
    {
        private readonly IStoreService _storeService;
        private readonly ICustomerService _customerService;
        private readonly Nop.Services.Customers.ICustomerRegistrationService _customerRegistrationService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IStoreDomainMappingService _domainMappingService;

        public TenantProvisioningService(
            IStoreService storeService,
            ICustomerService customerService,
            Nop.Services.Customers.ICustomerRegistrationService customerRegistrationService,
            IGenericAttributeService genericAttributeService,
            IStoreDomainMappingService domainMappingService)
        {
            _storeService = storeService;
            _customerService = customerService;
            _customerRegistrationService = customerRegistrationService;
            _genericAttributeService = genericAttributeService;
            _domainMappingService = domainMappingService;
        }

        public async Task<bool> ValidateSubdomainAvailabilityAsync(string subdomain)
        {
            if (string.IsNullOrWhiteSpace(subdomain)) return false;
            var cleanSubdomain = subdomain.Trim().ToLowerInvariant();

            // بررسی لیست واژه‌های رزرو شده
            var reservedNames = new[] { "admin", "api", "www", "app", "mail", "master", "dashboard", "billing" };
            if (System.Array.Exists(reservedNames, r => r.Equals(cleanSubdomain)))
                return false;

            var fullHost = $"{cleanSubdomain}.market.com";
            var existing = await _domainMappingService.GetByHostNameAsync(fullHost);
            return existing == null;
        }

        /// <summary>
        /// ⚠️ نسخهٔ قبلی این متد فرض می‌کرد مشتری از قبل با همان ایمیل ثبت‌نام کرده؛ اگر مشتری
        /// جدید بود (سناریوی عادی برای «ثبت‌نام مستقیم» یک فروشنده)، هیچ Customer‌ای ساخته نمی‌شد،
        /// یعنی فروشگاه تازه‌ساز عملاً هیچ مالکی نداشت. حالا اگر مشتری با این ایمیل وجود نداشته
        /// باشد، یک حساب واقعی (با رمز عبور واقعی از طریق ICustomerRegistrationService، نه دستکاری
        /// مستقیم فیلد) ساخته می‌شود.
        /// </summary>
        public async Task<ProvisioningResult> ProvisionNewTenantStoreAsync(ProvisionTenantRequest request)
        {
            if (!await ValidateSubdomainAvailabilityAsync(request.Subdomain))
            {
                return new ProvisioningResult
                {
                    Success = false,
                    ErrorMessage = "زیردامنه درخواستی قبلاً رزرو شده یا نامعتبر است."
                };
            }

            var fullHost = $"{request.Subdomain.Trim().ToLowerInvariant()}.market.com";
            var storeUrl = $"https://{fullHost}/";

            // ۱. ایجاد موجودیت Store جدید در nopCommerce
            var store = new Store
            {
                Name = request.StoreName,
                Url = storeUrl,
                SslEnabled = true,
                Hosts = fullHost,
                DisplayOrder = 10,
                CompanyName = request.CompanyName ?? request.StoreName
            };

            await _storeService.InsertStoreAsync(store);

            // ۲. ثبت نگاشت اولیه دامنه
            var domainMapping = new StoreDomainMapping
            {
                StoreId = store.Id,
                HostName = fullHost,
                IsPrimaryDomain = true,
                IsActive = true,
                IsSslVerified = true,
                CreatedOnUtc = DateTime.UtcNow
            };
            await _domainMappingService.InsertMappingAsync(domainMapping);

            // ۳. پیدا یا ساختن حساب مشتری (مالک فروشگاه)
            var customer = await _customerService.GetCustomerByEmailAsync(request.AdminEmail);
            var isNewCustomer = customer == null;

            if (customer == null)
            {
                if (string.IsNullOrWhiteSpace(request.Password))
                {
                    return new ProvisioningResult
                    {
                        Success = false,
                        ErrorMessage = "برای ساخت حساب کاربری جدید، رمز عبور الزامی است."
                    };
                }

                customer = new Customer
                {
                    CustomerGuid = Guid.NewGuid(),
                    Email = request.AdminEmail,
                    Username = request.AdminEmail,
                    Active = true,
                    CreatedOnUtc = DateTime.UtcNow,
                    LastActivityDateUtc = DateTime.UtcNow
                };
                await _customerService.InsertCustomerAsync(customer);

                var registeredRole = await _customerService.GetCustomerRoleBySystemNameAsync(NopCustomerDefaults.RegisteredRoleName);
                if (registeredRole != null)
                {
                    await _customerService.AddCustomerRoleMappingAsync(new CustomerCustomerRoleMapping
                    {
                        CustomerId = customer.Id,
                        CustomerRoleId = registeredRole.Id
                    });
                }

                // ⚠️ امضای دقیق CustomerRegistrationRequest باید بعد از build واقعی nopCommerce
                // 4.90.6 تایید شود (این بخش هیچ نمونهٔ قبلی در کدبیس نداشت).
                var registrationRequest = new Nop.Services.Customers.CustomerRegistrationRequest(
                    customer, request.AdminEmail, request.AdminEmail, request.Password,
                    Nop.Core.Domain.Customers.PasswordFormat.Hashed, store.Id, true);

                var registrationResult = await _customerRegistrationService.RegisterAsync(registrationRequest);
                if (!registrationResult.Success)
                {
                    return new ProvisioningResult
                    {
                        Success = false,
                        ErrorMessage = string.Join(" ", registrationResult.Errors)
                    };
                }
            }

            customer.RegisteredInStoreId = store.Id;
            await _customerService.UpdateCustomerAsync(customer);

            if (!string.IsNullOrWhiteSpace(request.AdminPhoneNumber))
                await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.PhoneAttribute, request.AdminPhoneNumber);

            // ثبت HomeStoreId به عنوان Generic Attribute جهت قفل مشتری به فروشگاه
            await _genericAttributeService.SaveAttributeAsync(customer, "HomeStoreId", store.Id);
            await _genericAttributeService.SaveAttributeAsync(customer, "IsTenantOwner", true);

            return new ProvisioningResult
            {
                Success = true,
                StoreId = store.Id,
                StoreUrl = storeUrl,
                PrimaryHostName = fullHost,
                OwnerCustomerId = customer.Id,
                IsNewCustomer = isNewCustomer
            };
        }

        public async Task ActivateTenantStoreAsync(int storeId)
        {
            var store = await _storeService.GetStoreByIdAsync(storeId);
            if (store == null) return;

            var mappings = await _domainMappingService.GetMappingsByStoreIdAsync(storeId);
            foreach (var mapping in mappings)
            {
                mapping.IsActive = true;
                await _domainMappingService.UpdateMappingAsync(mapping);
            }
        }

        public async Task SuspendTenantStoreAsync(int storeId, string reason)
        {
            var mappings = await _domainMappingService.GetMappingsByStoreIdAsync(storeId);
            foreach (var mapping in mappings)
            {
                mapping.IsActive = false;
                await _domainMappingService.UpdateMappingAsync(mapping);
            }
        }
    }

    public class ProvisionTenantRequest
    {
        public string StoreName { get; set; }
        public string Subdomain { get; set; }
        public string AdminEmail { get; set; }
        public string AdminPhoneNumber { get; set; }
        public string CompanyName { get; set; }
        public int PlanId { get; set; }

        /// <summary>الزامی فقط وقتی مشتری با این ایمیل هنوز وجود ندارد (سناریوی ثبت‌نام مستقیم).</summary>
        public string Password { get; set; }
    }

    public class ProvisioningResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int StoreId { get; set; }
        public string StoreUrl { get; set; }
        public string PrimaryHostName { get; set; }
        public int OwnerCustomerId { get; set; }
        public bool IsNewCustomer { get; set; }
    }
}