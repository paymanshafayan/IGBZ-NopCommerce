namespace Nop.Plugin.Misc.MultiTenantStores.Consumers
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Nop.Core;
    using Nop.Core.Domain.Catalog;
    using Nop.Core.Events;
    using Nop.Data;
    using Nop.Services.Events;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// به‌محض ثبت یا ویرایش محصول، یک رکورد در صف پس‌زمینهٔ همگام‌سازی مارکت‌پلیس ثبت می‌کند
    /// (برای هر Providerِ فعال دیجی‌کالا/دیوار) به‌جای فراخوانی هم‌زمان API بیرونی که پنل ادمین
    /// را کند می‌کند.
    /// </summary>
    public class ProductChangedMarketplaceSyncConsumer :
        IConsumer<EntityInsertedEvent<Product>>,
        IConsumer<EntityUpdatedEvent<Product>>
    {
        private readonly IStoreContext _storeContext;
        private readonly ITenantIntegrationCredentialService _credentialService;
        private readonly IRepository<PendingMarketplaceSync> _syncQueueRepository;

        public ProductChangedMarketplaceSyncConsumer(
            IStoreContext storeContext,
            ITenantIntegrationCredentialService credentialService,
            IRepository<PendingMarketplaceSync> syncQueueRepository)
        {
            _storeContext = storeContext;
            _credentialService = credentialService;
            _syncQueueRepository = syncQueueRepository;
        }

        public async Task HandleEventAsync(EntityInsertedEvent<Product> eventMessage)
        {
            await EnqueueAsync(eventMessage.Entity, MarketplaceSyncAction.Publish);
        }

        public async Task HandleEventAsync(EntityUpdatedEvent<Product> eventMessage)
        {
            await EnqueueAsync(eventMessage.Entity, MarketplaceSyncAction.CreateOrUpdatePrice);
        }

        private async Task EnqueueAsync(Product product, MarketplaceSyncAction action)
        {
            if (product == null || product.Deleted) return;

            var currentStore = await _storeContext.GetCurrentStoreAsync();
            var credentials = await _credentialService.GetByStoreIdAsync(currentStore.Id);

            foreach (var providerKey in new[] { "digikala", "divar" })
            {
                var credential = credentials.FirstOrDefault(c => c.ProviderKey == providerKey && c.IsActive);
                if (credential == null) continue; // بدون اعتبارنامهٔ فعال، هیچ صفی ساخته نمی‌شود

                await _syncQueueRepository.InsertAsync(new PendingMarketplaceSync
                {
                    StoreId = currentStore.Id,
                    ProductId = product.Id,
                    ProviderKey = providerKey,
                    Action = action,
                    IsProcessed = false,
                    AttemptCount = 0,
                    CreatedOnUtc = DateTime.UtcNow
                });
            }
        }
    }
}
