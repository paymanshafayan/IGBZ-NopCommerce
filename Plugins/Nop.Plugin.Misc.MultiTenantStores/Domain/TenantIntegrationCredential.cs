namespace Nop.Plugin.Misc.MultiTenantStores.Domain
{
    using System;
    using Nop.Core;

    /// <summary>
    /// نگهداری اعتبارنامه (API Key/Secret) هر تننت برای هر سرویس بیرونی.
    /// این موجودیت جایگزین مقادیر Hardcode شده در سرویس‌های یکپارچه‌سازی می‌شود؛
    /// بدون رکورد فعال و تاییدشده، هیچ سرویسی نباید عملیات را «موفق» تلقی کند.
    /// </summary>
    public class TenantIntegrationCredential : BaseEntity
    {
        public int StoreId { get; set; }

        /// <summary>
        /// شناسه یکتای Provider، مثل: "parbad.zarinpal", "digikala", "divar", "torob",
        /// "snapppay", "nowpayments", "tapin"
        /// </summary>
        public string ProviderKey { get; set; }

        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }

        /// <summary>آدرس Endpoint در صورتی که برای هر تننت متفاوت باشد (اختیاری)</summary>
        public string EndpointOverrideUrl { get; set; }

        public bool IsActive { get; set; }
        public bool IsVerified { get; set; }
        public DateTime CreatedOnUtc { get; set; }
        public DateTime UpdatedOnUtc { get; set; }

        /// <summary>آخرین باری که یک تلاش واقعی برای تست اتصال انجام شد (موفق یا ناموفق)</summary>
        public DateTime? LastTestedOnUtc { get; set; }

        /// <summary>نتیجهٔ واقعی آخرین تلاش تست اتصال (نه یک پیام ثابت)</summary>
        public string LastTestResultMessage { get; set; }
    }

    /// <summary>
    /// دفترکل تراکنش‌های پرداخت جهت جلوگیری از تایید مضاعف (Double Verification) و Replay.
    /// هر Tracking Number فقط یک بار مجاز به تایید موفق است.
    /// </summary>
    public class PaymentTransactionLedger : BaseEntity
    {
        public int StoreId { get; set; }
        public int OrderId { get; set; }
        public string GatewayName { get; set; }
        public string TrackingNumber { get; set; }
        public decimal AmountToman { get; set; }

        public PaymentTransactionState State { get; set; }

        public string BankRefId { get; set; }
        public string RawGatewayResponse { get; set; }

        public DateTime RequestedOnUtc { get; set; }
        public DateTime? VerifiedOnUtc { get; set; }
    }

    public enum PaymentTransactionState
    {
        Requested = 0,
        RedirectedToBank = 10,
        VerifiedSuccess = 20,
        VerifiedFailed = 30,
        AlreadyVerified = 40
    }
}
