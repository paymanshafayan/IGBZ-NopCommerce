namespace Nop.Plugin.Misc.MultiTenantStores.Domain
{
    using System;
    using Nop.Core;

    /// <summary>
    /// ذخیره‌سازی پایدار (DB) کدهای یک‌بارمصرف ورود با شماره موبایل — جایگزین نگهداری فقط در
    /// حافظه (IMemoryCache) که با ری‌استارت اپلیکیشن یا در حالت چند نمونه (Multi-Instance) کدهای
    /// در انتظار تایید را از دست می‌داد. کد به‌صورت Hash (SHA-256 از storeId+phone+code) ذخیره
    /// می‌شود تا نشت دیتابیس به‌تنهایی کافی نباشد؛ رکورد پس از تایید موفق با Used=true باطل می‌شود.
    /// </summary>
    public class PhoneOtpCode : BaseEntity
    {
        public int StoreId { get; set; }
        public string PhoneNumber { get; set; }
        public string CodeHash { get; set; }
        public DateTime CreatedOnUtc { get; set; }
        public DateTime ExpiresOnUtc { get; set; }
        public bool Used { get; set; }
    }
}
