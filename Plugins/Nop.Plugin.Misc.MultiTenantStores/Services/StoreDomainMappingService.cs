namespace Nop.Plugin.Misc.MultiTenantStores.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Nop.Data;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;

    public interface IStoreDomainMappingService
    {
        Task<StoreDomainMapping> GetByHostNameAsync(string hostName);
        Task<IList<StoreDomainMapping>> GetMappingsByStoreIdAsync(int storeId);
        Task<IList<StoreDomainMapping>> GetAllMappingsAsync();
        Task InsertMappingAsync(StoreDomainMapping mapping);
        Task UpdateMappingAsync(StoreDomainMapping mapping);
        Task DeleteMappingAsync(StoreDomainMapping mapping);
        Task SetPrimaryDomainAsync(int storeId, int mappingId);
        Task<bool> VerifyCustomDomainCnameAsync(string domainName, string expectedTargetCname);
    }

    public class StoreDomainMappingService : IStoreDomainMappingService
    {
        private readonly IRepository<StoreDomainMapping> _mappingRepository;

        public StoreDomainMappingService(IRepository<StoreDomainMapping> mappingRepository)
        {
            _mappingRepository = mappingRepository;
        }

        public async Task<StoreDomainMapping> GetByHostNameAsync(string hostName)
        {
            if (string.IsNullOrWhiteSpace(hostName))
                return null;

            var cleanHost = hostName.Trim().ToLowerInvariant();
            
            // حذف پورت در صورت وجود (مانند store1.market.com:443)
            if (cleanHost.Contains(":"))
                cleanHost = cleanHost.Split(':')[0];

            return await _mappingRepository.Table
                .Where(m => m.IsActive && m.HostName.ToLower() == cleanHost)
                .FirstOrDefaultAsync();
        }

        public async Task<IList<StoreDomainMapping>> GetMappingsByStoreIdAsync(int storeId)
        {
            return await _mappingRepository.GetAllAsync(query =>
                query.Where(m => m.StoreId == storeId)
                     .OrderByDescending(m => m.IsPrimaryDomain)
                     .ThenBy(m => m.CreatedOnUtc));
        }

        public async Task<IList<StoreDomainMapping>> GetAllMappingsAsync()
        {
            return await _mappingRepository.GetAllAsync(query =>
                query.OrderByDescending(m => m.CreatedOnUtc));
        }

        public async Task InsertMappingAsync(StoreDomainMapping mapping)
        {
            if (mapping == null) throw new ArgumentNullException(nameof(mapping));

            mapping.HostName = mapping.HostName.Trim().ToLowerInvariant();
            mapping.CreatedOnUtc = DateTime.UtcNow;
            mapping.UpdatedOnUtc = DateTime.UtcNow;

            await _mappingRepository.InsertAsync(mapping);
        }

        public async Task UpdateMappingAsync(StoreDomainMapping mapping)
        {
            if (mapping == null) throw new ArgumentNullException(nameof(mapping));

            mapping.UpdatedOnUtc = DateTime.UtcNow;
            await _mappingRepository.UpdateAsync(mapping);
        }

        public async Task DeleteMappingAsync(StoreDomainMapping mapping)
        {
            if (mapping == null) throw new ArgumentNullException(nameof(mapping));
            await _mappingRepository.DeleteAsync(mapping);
        }

        public async Task SetPrimaryDomainAsync(int storeId, int mappingId)
        {
            var mappings = await GetMappingsByStoreIdAsync(storeId);
            foreach (var m in mappings)
            {
                m.IsPrimaryDomain = (m.Id == mappingId);
                m.UpdatedOnUtc = DateTime.UtcNow;
                await _mappingRepository.UpdateAsync(m);
            }
        }

        public async Task<bool> VerifyCustomDomainCnameAsync(string domainName, string expectedTargetCname)
        {
            if (string.IsNullOrWhiteSpace(domainName)) return false;

            try
            {
                // بررسی رکورد DNS CNAME
                var hostEntry = await System.Net.Dns.GetHostEntryAsync(domainName);
                return hostEntry != null && hostEntry.AddressList.Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}