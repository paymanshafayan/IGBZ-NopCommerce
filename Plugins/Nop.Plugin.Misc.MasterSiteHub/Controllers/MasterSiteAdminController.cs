namespace Nop.Plugin.Misc.MasterSiteHub.Controllers
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Data;
    using Nop.Core.Domain.Orders;
    using Nop.Services.Stores;
    using Nop.Services.Customers;
    using Nop.Services.Common;
    using Nop.Plugin.Misc.MultiTenantStores.Services;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;

    [ApiController]
    [Route("api/mastersite/admin")]
    public class MasterSiteAdminController : ControllerBase
    {
        private readonly IWorkContext _workContext;
        private readonly ICustomerService _customerService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IStoreService _storeService;
        private readonly IStoreDomainMappingService _domainMappingService;
        private readonly ITenantProvisioningService _tenantProvisioningService;
        private readonly ITenantPlanService _tenantPlanService;
        private readonly IRepository<Order> _orderRepository;
        private readonly IRepository<TenantStoreSubscription> _subscriptionRepository;

        public MasterSiteAdminController(
            IWorkContext workContext,
            ICustomerService customerService,
            IGenericAttributeService genericAttributeService,
            IStoreService storeService,
            IStoreDomainMappingService domainMappingService,
            ITenantProvisioningService tenantProvisioningService,
            ITenantPlanService tenantPlanService,
            IRepository<Order> orderRepository,
            IRepository<TenantStoreSubscription> subscriptionRepository)
        {
            _workContext = workContext;
            _customerService = customerService;
            _genericAttributeService = genericAttributeService;
            _storeService = storeService;
            _domainMappingService = domainMappingService;
            _tenantProvisioningService = tenantProvisioningService;
            _tenantPlanService = tenantPlanService;
            _orderRepository = orderRepository;
            _subscriptionRepository = subscriptionRepository;
        }

        private async Task<bool> IsSuperAdminAsync()
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            if (customer == null) return false;
            var isTenantOwner = await _genericAttributeService.GetAttributeAsync<bool>(customer, "IsTenantOwner");
            return await _customerService.IsAdminAsync(customer) && !isTenantOwner;
        }

        /// <summary>
        /// داشبورد آمار کل سکو و شاخص‌های مالی — تمام مقادیر از داده واقعی محاسبه می‌شوند،
        /// نه فرمول‌های فرضی (نسخه قبلی activeStoresCount * 850000 و اعداد ثابت داشت).
        /// </summary>
        [HttpGet("dashboard-summary")]
        public async Task<IActionResult> GetDashboardSummary()
        {
            if (!await IsSuperAdminAsync()) return Forbid();

            var stores = await _storeService.GetAllStoresAsync();
            var allDomainMappings = await _domainMappingService.GetAllMappingsAsync();
            var allActivePlans = await _tenantPlanService.GetAllActivePlansAsync();
            var activeSubscriptions = await _subscriptionRepository.GetAllAsync(q =>
                q.Where(s => s.Status == SubscriptionStatus.Active));

            // MRR واقعی = مجموع قیمت ماهانه پلن هر اشتراک فعال (Join واقعی با جدول پلن‌ها)
            var monthlyRecurringRevenueToman = activeSubscriptions
                .Join(allActivePlans, s => s.TenantPlanId, p => p.Id, (s, p) => p.PriceMonthly)
                .Sum();

            var pendingSslCount = allDomainMappings.Count(m => m.IsActive && !m.IsSslVerified);
            var totalOrdersProcessed = await _orderRepository.GetAllAsync(q => q, getCacheKey: null);

            var summary = new
            {
                TotalTenantStores = stores.Count,
                TotalDomainMappings = allDomainMappings.Count,
                MonthlyRecurringRevenueToman = monthlyRecurringRevenueToman,
                PendingCustomDomainsSsl = pendingSslCount,
                TotalOrdersProcessedAllTime = totalOrdersProcessed.Count
            };

            return Ok(summary);
        }

        /// <summary>
        /// لیست کامل دامنه‌های اختصاصی مشتریان — از ریپازیتوری واقعی خوانده می‌شود
        /// (نسخه قبلی سه رکورد نمونه Hardcode شده برمی‌گرداند، علی‌رغم وجود سرویس واقعی تزریق‌شده).
        /// </summary>
        [HttpGet("domain-mappings")]
        public async Task<IActionResult> GetDomainMappings()
        {
            if (!await IsSuperAdminAsync()) return Forbid();

            var mappings = await _domainMappingService.GetAllMappingsAsync();
            var stores = await _storeService.GetAllStoresAsync();

            var result = mappings.Select(m => new
            {
                m.Id,
                m.StoreId,
                StoreName = stores.FirstOrDefault(s => s.Id == m.StoreId)?.Name ?? $"فروشگاه #{m.StoreId}",
                HostName = m.HostName,
                IsPrimary = m.IsPrimaryDomain,
                IsSslVerified = m.IsSslVerified,
                IsActive = m.IsActive,
                CreatedOn = m.CreatedOnUtc
            });

            return Ok(result);
        }

        /// <summary>
        /// تایید واقعی رکورد DNS CNAME دامنه اختصاصی مشتری از طریق StoreDomainMappingService
        /// (نسخه قبلی بدون هیچ بررسی، همیشه success=true برمی‌گرداند).
        /// </summary>
        [HttpPost("verify-domain-ssl/{mappingId}")]
        public async Task<IActionResult> VerifyDomainSsl(int mappingId)
        {
            if (!await IsSuperAdminAsync()) return Forbid();

            var mappings = await _domainMappingService.GetAllMappingsAsync();
            var mapping = mappings.FirstOrDefault(m => m.Id == mappingId);
            if (mapping == null)
                return NotFound(new { message = "نگاشت دامنه یافت نشد." });

            var expectedCname = "tenants.igbz.ir";
            var isCnameValid = await _domainMappingService.VerifyCustomDomainCnameAsync(mapping.HostName, expectedCname);

            if (!isCnameValid)
            {
                return Ok(new
                {
                    success = false,
                    message = $"رکورد CNAME دامنه {mapping.HostName} هنوز به {expectedCname} اشاره نمی‌کند. صدور SSL امکان‌پذیر نیست."
                });
            }

            mapping.IsSslVerified = true;
            mapping.UpdatedOnUtc = DateTime.UtcNow;
            await _domainMappingService.UpdateMappingAsync(mapping);

            return Ok(new { success = true, message = "رکورد CNAME تایید شد. صدور گواهی SSL (Let's Encrypt) در صف پردازش قرار گرفت." });
        }

        /// <summary>
        /// تعلیق دسترسی یا فعال‌سازی مجدد فروشگاه مستاجر
        /// </summary>
        [HttpPost("toggle-tenant-status")]
        public async Task<IActionResult> ToggleTenantStatus([FromBody] TenantStatusToggleDto dto)
        {
            if (!await IsSuperAdminAsync()) return Forbid();

            if (dto.IsSuspend)
            {
                await _tenantProvisioningService.SuspendTenantStoreAsync(dto.StoreId, dto.Reason ?? "انقضای اشتراک یا درخواست سوپرادمین");
                return Ok(new { success = true, message = $"فروشگاه شناسه {dto.StoreId} تعلیق شد." });
            }
            else
            {
                await _tenantProvisioningService.ActivateTenantStoreAsync(dto.StoreId);
                return Ok(new { success = true, message = $"فروشگاه شناسه {dto.StoreId} مجدداً فعال گردید." });
            }
        }
    }

    public class TenantStatusToggleDto
    {
        public int StoreId { get; set; }
        public bool IsSuspend { get; set; }
        public string Reason { get; set; }
    }
}