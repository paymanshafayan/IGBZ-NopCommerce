namespace Nop.Plugin.Misc.MultiTenantStores.Domain
{
    using System;
    using Nop.Core;

    /// <summary>
    /// صف پس‌زمینهٔ همگام‌سازی محصول با مارکت‌پلیس‌ها (دیجی‌کالا/دیوار). طبق راهنمای
    /// «اتصال فروشگاه به ترب و دیجی‌کالا»: ارسال هم‌زمان در لحظهٔ ذخیرهٔ محصول باعث کندی پنل ادمین
    /// می‌شود؛ به‌جایش رکورد در این جدول ثبت و توسط <see cref="MarketplaceSyncScheduleTask"/>
    /// در پس‌زمینه پردازش می‌شود.
    /// </summary>
    public class PendingMarketplaceSync : BaseEntity
    {
        public int StoreId { get; set; }
        public int ProductId { get; set; }
        public string ProviderKey { get; set; } // "digikala" | "divar"
        public MarketplaceSyncAction Action { get; set; }
        public bool IsProcessed { get; set; }
        public string LastError { get; set; }
        public int AttemptCount { get; set; }
        public DateTime CreatedOnUtc { get; set; }
        public DateTime? ProcessedOnUtc { get; set; }
    }

    public enum MarketplaceSyncAction
    {
        CreateOrUpdatePrice = 0,
        Publish = 10
    }
}
