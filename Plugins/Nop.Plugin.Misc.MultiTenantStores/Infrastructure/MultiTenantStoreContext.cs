namespace Nop.Plugin.Misc.MultiTenantStores.Infrastructure
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Net.Http.Headers;
    using Nop.Core;
    using Nop.Core.Domain.Stores;
    using Nop.Data;
    using Nop.Services.Stores;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// پیاده‌سازی اختصاصی <see cref="IStoreContext"/> با پشتیبانی از نگاشت دامنهٔ چندمستأجری
    /// (بخش ۴ سند معماری). جایگزین <c>WebStoreContext</c> پیش‌فرض nopCommerce می‌شود.
    /// ترتیب تشخیص فروشگاه: هدر X-Store-Id (موبایل/API) → نگاشت دامنهٔ اختصاصی تننت →
    /// ستون Hosts پیش‌فرض nopCommerce → اولین فروشگاه (Fallback).
    /// </summary>
    public class MultiTenantStoreContext : IStoreContext
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IStoreService _storeService;
        private readonly IStoreDomainMappingService _domainMappingService;
        private readonly IRepository<Store> _storeRepository;

        private Store _cachedStore;
        private int? _cachedActiveStoreScopeConfiguration;

        public MultiTenantStoreContext(
            IHttpContextAccessor httpContextAccessor,
            IStoreService storeService,
            IStoreDomainMappingService domainMappingService,
            IRepository<Store> storeRepository)
        {
            _httpContextAccessor = httpContextAccessor;
            _storeService = storeService;
            _domainMappingService = domainMappingService;
            _storeRepository = storeRepository;
        }

        public async Task<Store> GetCurrentStoreAsync()
        {
            if (_cachedStore != null)
                return _cachedStore;

            var httpContext = _httpContextAccessor.HttpContext;
            var allStores = await _storeService.GetAllStoresAsync();

            if (httpContext == null)
            {
                // فراخوانی خارج از وب‌ریکوئست (کارهای پس‌زمینه/Scheduled Task) -> اولین فروشگاه
                _cachedStore = allStores.FirstOrDefault();
                return _cachedStore ?? throw new Exception("هیچ فروشگاهی برای بارگذاری یافت نشد.");
            }

            // ۱. هدر X-Store-Id (درخواست‌های REST API و اپلیکیشن موبایل)
            if (httpContext.Request.Headers.TryGetValue("X-Store-Id", out var storeIdHeader) &&
                int.TryParse(storeIdHeader.FirstOrDefault(), out var apiStoreId) && apiStoreId > 0)
            {
                var storeByHeader = allStores.FirstOrDefault(s => s.Id == apiStoreId);
                if (storeByHeader != null)
                {
                    _cachedStore = storeByHeader;
                    return _cachedStore;
                }
            }

            var host = httpContext.Request.Headers[HeaderNames.Host].ToString();

            // ۲. نگاشت دامنهٔ اختصاصی تننت (bereits StoreDomainMapping)
            if (!string.IsNullOrEmpty(host))
            {
                var domainMapping = await _domainMappingService.GetByHostNameAsync(host);
                if (domainMapping != null && domainMapping.IsActive)
                {
                    var storeByDomain = allStores.FirstOrDefault(s => s.Id == domainMapping.StoreId);
                    if (storeByDomain != null)
                    {
                        _cachedStore = storeByDomain;
                        return _cachedStore;
                    }
                }
            }

            // ۳. ستون Hosts پیش‌فرض nopCommerce + Fallback به اولین فروشگاه
            var store = allStores.FirstOrDefault(s => _storeService.ContainsHostValue(s, host)) ?? allStores.FirstOrDefault();
            _cachedStore = store ?? throw new Exception("هیچ فروشگاهی برای بارگذاری یافت نشد.");

            return _cachedStore;
        }

        /// <summary>
        /// نسخهٔ همزمان (Sync) — طبق قرارداد IStoreContext نباید متد Async را Block کند، بلکه از
        /// Repository همزمان (GetAll غیر Async) استفاده می‌کند، دقیقاً مثل WebStoreContext اصلی.
        /// نگاشت دامنهٔ تننت در این مسیر بررسی نمی‌شود (فقط ستون Hosts) — محدودیت شناخته‌شده که در
        /// عمل مشکلی ایجاد نمی‌کند چون تمام درخواست‌های HTTP واقعی از مسیر Async عبور می‌کنند؛ این
        /// متد فقط برای فراخوانی‌های همزمان قدیمی داخل خودِ nopCommerce وجود دارد.
        /// </summary>
        public Store GetCurrentStore()
        {
            if (_cachedStore != null)
                return _cachedStore;

            var host = _httpContextAccessor.HttpContext?.Request.Headers[HeaderNames.Host].ToString();

            var allStores = _storeRepository.GetAll(query =>
                from s in query orderby s.DisplayOrder, s.Id select s,
                _ => default, includeDeleted: false);

            var store = allStores.FirstOrDefault(s => _storeService.ContainsHostValue(s, host)) ?? allStores.FirstOrDefault();
            _cachedStore = store ?? throw new Exception("هیچ فروشگاهی برای بارگذاری یافت نشد.");

            return _cachedStore;
        }

        public async Task<int> GetActiveStoreScopeConfigurationAsync()
        {
            if (_cachedActiveStoreScopeConfiguration.HasValue)
                return _cachedActiveStoreScopeConfiguration.Value;

            var store = await GetCurrentStoreAsync();
            _cachedActiveStoreScopeConfiguration = store?.Id ?? 0;

            return _cachedActiveStoreScopeConfiguration.Value;
        }
    }
}
