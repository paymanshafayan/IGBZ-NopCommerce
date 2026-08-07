namespace Nop.Plugin.Misc.MultiTenantStores.Domain
{
    using System;
    using Nop.Core;

    /// <summary>
    /// موجودیت نگاشت دامنه/زیردامنه به فروشگاه‌های مالتی‌تننت
    /// </summary>
    public class StoreDomainMapping : BaseEntity
    {
        /// <summary>
        /// شناسه فروشگاه متناظر در nopCommerce (Store.Id)
        /// </summary>
        public int StoreId { get; set; }

        /// <summary>
        /// دامنه یا زیردامنه کامل (مانند: store1.market.com یا mybrand.com)
        /// </summary>
        public string HostName { get; set; }

        /// <summary>
        /// آیا این دامنه، دامنه اصلی و برندینگ اول فروشگاه است؟
        /// </summary>
        public bool IsPrimaryDomain { get; set; }

        /// <summary>
        /// وضعیت فعال بودن نگاشت
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// وضعیت صحت سنجی گواهی SSL و رکورد CNAME در صورت استفاده از دامنه اختصاصی مشتری
        /// </summary>
        public bool IsSslVerified { get; set; }

        /// <summary>
        /// تاریخ ثبت دامنه
        /// </summary>
        public DateTime CreatedOnUtc { get; set; }

        /// <summary>
        /// تاریخ آخرین ویرایش
        /// </summary>
        public DateTime UpdatedOnUtc { get; set; }
    }
}