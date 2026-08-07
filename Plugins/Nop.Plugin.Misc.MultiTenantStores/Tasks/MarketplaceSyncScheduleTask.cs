namespace Nop.Plugin.Misc.MultiTenantStores.Tasks
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Nop.Core.Domain.Catalog;
    using Nop.Data;
    using Nop.Services.Catalog;
    using Nop.Services.Common;
    using Nop.Services.ScheduleTasks;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// هر چند دقیقه یک‌بار صف <see cref="PendingMarketplaceSync"/> را پردازش می‌کند و واقعاً به
    /// دیجی‌کالا/دیوار متصل می‌شود. بدون این Task، سرویس‌های Marketplace فقط کد بلااستفاده بودند.
    /// </summary>
    public class MarketplaceSyncScheduleTask : IScheduleTask
    {
        private const int MaxAttempts = 5;
        private const int BatchSize = 50;

        private readonly IRepository<PendingMarketplaceSync> _syncQueueRepository;
        private readonly IRepository<TenantIntegrationCredential> _credentialRepository;
        private readonly ITenantIntegrationCredentialService _credentialService;
        private readonly IMarketplaceOmnichannelService _marketplaceService;
        private readonly IProductService _productService;
        private readonly IGenericAttributeService _genericAttributeService;

        public const string DigikalaVariantIdAttributeKey = "DigikalaVariantId";

        public MarketplaceSyncScheduleTask(
            IRepository<PendingMarketplaceSync> syncQueueRepository,
            IRepository<TenantIntegrationCredential> credentialRepository,
            ITenantIntegrationCredentialService credentialService,
            IMarketplaceOmnichannelService marketplaceService,
            IProductService productService,
            IGenericAttributeService genericAttributeService)
        {
            _syncQueueRepository = syncQueueRepository;
            _credentialRepository = credentialRepository;
            _credentialService = credentialService;
            _marketplaceService = marketplaceService;
            _productService = productService;
            _genericAttributeService = genericAttributeService;
        }

        public async Task ExecuteAsync()
        {
            var pendingItems = await _syncQueueRepository.GetAllAsync(q =>
                q.Where(x => !x.IsProcessed && x.AttemptCount < MaxAttempts)
                 .OrderBy(x => x.CreatedOnUtc)
                 .Take(BatchSize));

            foreach (var item in pendingItems)
            {
                try
                {
                    var product = await _productService.GetProductByIdAsync(item.ProductId);
                    if (product == null || product.Deleted)
                    {
                        item.IsProcessed = true;
                        item.LastError = "محصول حذف شده بود.";
                        await _syncQueueRepository.UpdateAsync(item);
                        continue;
                    }

                    var credential = (await _credentialRepository.GetAllAsync(q =>
                        q.Where(c => c.StoreId == item.StoreId && c.ProviderKey == item.ProviderKey && c.IsActive)))
                        .FirstOrDefault();

                    if (credential == null)
                    {
                        item.IsProcessed = true;
                        item.LastError = "اعتبارنامه دیگر فعال نیست.";
                        await _syncQueueRepository.UpdateAsync(item);
                        continue;
                    }

                    var apiKey = _credentialService.DecryptForActualUse(credential.ApiKey);
                    var success = await ProcessOneAsync(item, product, apiKey, credential);

                    item.AttemptCount++;
                    item.IsProcessed = success;
                    item.ProcessedOnUtc = success ? DateTime.UtcNow : (DateTime?)null;
                    item.LastError = success ? null : "فراخوانی API مارکت‌پلیس ناموفق بود.";
                    await _syncQueueRepository.UpdateAsync(item);
                }
                catch (Exception ex)
                {
                    item.AttemptCount++;
                    item.LastError = ex.Message;
                    await _syncQueueRepository.UpdateAsync(item);
                }
            }
        }

        private async Task<bool> ProcessOneAsync(PendingMarketplaceSync item, Product product, string apiKey, TenantIntegrationCredential credential)
        {
            if (item.ProviderKey == "digikala")
            {
                // شناسهٔ Variant دیجی‌کالا باید از قبل توسط فروشنده Map شده باشد. قبلاً از
                // product.Sku به‌عنوان میان‌بر استفاده می‌شد که SKU داخلی خودِ فروشگاه را با شناسهٔ
                // بیرونی دیجی‌کالا قاطی می‌کرد و برای محصولاتی که در چند مارکت‌پلیس با شناسه‌های
                // متفاوت لیست شده‌اند اصلاً کار نمی‌کرد.
                var digikalaVariantId = await _genericAttributeService.GetAttributeAsync<string>(
                    product, DigikalaVariantIdAttributeKey);
                if (string.IsNullOrEmpty(digikalaVariantId))
                    return false;

                return await _marketplaceService.SyncStockAndPriceWithDigikalaAsync(
                    apiKey, digikalaVariantId, product.StockQuantity, product.Price);
            }

            if (item.ProviderKey == "divar")
            {
                var result = await _marketplaceService.PublishPostOnKenarDivarAsync(
                    apiKey, product.Name, product.ShortDescription ?? product.Name, product.Price, imageUrl: null);
                return result.IsSuccess;
            }

            return false;
        }
    }
}
