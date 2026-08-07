namespace Nop.Plugin.Misc.MultiTenantStores.Domain
{
    using System;
    using Nop.Core;

    /// <summary>کد معرف متنی کوتاه اختصاصی هر مشتری (مثل ALI100) — طبق راهنمای Affiliate Marketing</summary>
    public class AffiliateReferralCode : BaseEntity
    {
        public int CustomerId { get; set; }
        public int StoreId { get; set; }
        public string Code { get; set; }
        public DateTime CreatedOnUtc { get; set; }
    }

    /// <summary>
    /// دفترکل واقعی کمیسیون‌های Affiliate — موجودی معرف همیشه SUM این جدول است، دقیقاً مطابق
    /// همان الگوی Ledger که برای کیف‌پول/پرداخت در بقیهٔ پلتفرم استفاده شده.
    /// </summary>
    public class AffiliateCommissionLedger : BaseEntity
    {
        public int ReferrerCustomerId { get; set; }
        public int ReferredCustomerId { get; set; }
        public int StoreId { get; set; }
        public int OrderId { get; set; }
        public decimal CommissionToman { get; set; }
        public AffiliateCommissionState State { get; set; }
        public DateTime CreatedOnUtc { get; set; }
    }

    public enum AffiliateCommissionState
    {
        Earned = 0,
        WithdrawalRequested = 10,
        Paid = 20
    }

    /// <summary>درخواست تسویه‌حساب (برداشت) معرف — برای پنل ادمین «مدیریت درخواست‌های تسویه»</summary>
    public class AffiliateWithdrawalRequest : BaseEntity
    {
        public int CustomerId { get; set; }
        public int StoreId { get; set; }
        public decimal AmountToman { get; set; }
        public AffiliateWithdrawalStatus Status { get; set; }
        public string BankAccountInfo { get; set; }
        public string AdminNote { get; set; }
        public DateTime RequestedOnUtc { get; set; }
        public DateTime? ProcessedOnUtc { get; set; }
    }

    public enum AffiliateWithdrawalStatus
    {
        Requested = 0,
        Approved = 10,
        Rejected = 20,
        Paid = 30
    }
}
