namespace Nop.Plugin.Misc.MultiTenantStores.Domain
{
    using System;
    using Nop.Core;

    /// <summary>
    /// تعریف پلن‌های اشتراکی ارائه شده در سایت مادر
    /// </summary>
    public class TenantPlan : BaseEntity
    {
        public string Name { get; set; }
        public string SystemName { get; set; }
        public string Description { get; set; }

        /// <summary>
        /// شناسه محصول متناظر در nopCommerce برای دریافت وجه و درگاه پرداخت
        /// </summary>
        public int LinkedProductId { get; set; }

        public int MaxProductsAllowed { get; set; }
        public int MaxOrdersPerMonth { get; set; }
        public bool AllowCustomDomain { get; set; }

        /// <summary>شامل اپلیکیشن موبایل اختصاصی (Android/iOS) — طبق ساختار پلن‌های برنزی/نقره‌ای/طلایی.</summary>
        public bool AllowDedicatedApp { get; set; }

        /// <summary>شامل فروشگاه — همهٔ پلن‌ها این را دارند، ولی به‌عنوان یک Flag صریح نگه داشته می‌شود تا در پنل مدیریت قابل نمایش/توضیح باشد.</summary>
        public bool AllowStore { get; set; }

        /// <summary>دستیار اینستاگرام نسخهٔ عادی (تولید محتوای AI، پاسخ خودکار کامنت و...).</summary>
        public bool AllowInstagramAiAssistant { get; set; }

        /// <summary>
        /// دستیار اینستاگرام نسخهٔ Pro — مشتریان VIP برای ویدیوهای ویژهٔ اشتراکی + حمایت مالی از طریق
        /// کامنت. اگر true باشد، AllowInstagramAiAssistant هم باید true باشد (Pro شامل عادی است).
        /// </summary>
        public bool AllowInstagramAiAssistantPro { get; set; }

        public decimal PriceMonthly { get; set; }
        public decimal PriceSixMonths { get; set; }
        public decimal PriceYearly { get; set; }

        /// <summary>اگر بزرگ‌تر از صفر باشد، این یک پلن آزمایشی رایگان با همین تعداد روز اعتبار است (نه یک پلن پولی عادی).</summary>
        public int TrialDurationDays { get; set; }

        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// وضعیت اشتراک جاری هر فروشگاه
    /// </summary>
    public class TenantStoreSubscription : BaseEntity
    {
        public int StoreId { get; set; }
        public int TenantPlanId { get; set; }

        /// <summary>مشتری مالک تننت (Tenant Owner) — برای صدور فاکتور/اطلاع‌رسانی مستقیم، بدون نیاز به جست‌وجوی معکوس بین تمام مشتریان فروشگاه</summary>
        public int OwnerCustomerId { get; set; }

        public SubscriptionStatus Status { get; set; }

        public DateTime TrialEndDateUtc { get; set; }
        public DateTime StartDateUtc { get; set; }
        public DateTime NextBillingDateUtc { get; set; }
        public bool AutoRenew { get; set; }

        public DateTime CreatedOnUtc { get; set; }
        public DateTime UpdatedOnUtc { get; set; }
    }

    public enum SubscriptionStatus
    {
        PendingPayment = 0,
        Trial = 10,
        Active = 20,
        PastDue = 30,
        Suspended = 40,
        Cancelled = 50
    }

    public enum BillingCycle
    {
        Monthly = 0,
        SixMonths = 1,
        Yearly = 2
    }
}