namespace Nop.Plugin.Misc.MultiTenantStores.Domain
{
    using System;
    using Nop.Core;

    /// <summary>
    /// اعتبارنامه‌های BNPL (دیجی‌پی/اسنپ‌پی) — جدا از Credential عمومی، مخصوص تنظیمات OAuth هر ارائه‌دهنده.
    /// (اختیاری: می‌توان از TenantIntegrationCredential هم استفاده کرد؛ این جدول برای وضوح تنظیمات BNPL است.)
    /// </summary>
    public class BnplCredential : BaseEntity
    {
        public int StoreId { get; set; }

        /// <summary>"digipay" یا "snapppay"</summary>
        public string ProviderKey { get; set; }

        public string Username { get; set; }
        public string Password { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }

        /// <summary>محیط: "uat" یا "live" (برای دیجی‌پی)؛ برای اسنپ‌پی BaseUrl مستقیم.</summary>
        public string Environment { get; set; }

        /// <summary>برای اسنپ‌پی: آدرس BaseUrl سفارشی (اگر خالی بود، پیش‌فرض Staging).</summary>
        public string BaseUrlOverride { get; set; }

        public bool IsActive { get; set; }
        public DateTime CreatedOnUtc { get; set; }
        public DateTime UpdatedOnUtc { get; set; }
    }

    /// <summary>اجازهٔ BNPL (Eligibility) — ثبت نتیجهٔ استعلام اعتبار به‌ازای هر مشتری/سبد.</summary>
    public class BnplEligibilityRecord : BaseEntity
    {
        public int StoreId { get; set; }
        public int CustomerId { get; set; }
        public string ProviderKey { get; set; }

        /// <summary>مبلغ سبد به تومان.</summary>
        public decimal AmountToman { get; set; }

        public bool IsEligible { get; set; }
        public string ResponseJson { get; set; }
        public DateTime CheckedOnUtc { get; set; }
    }

    /// <summary>توکن/تیکت پرداخت BNPL و وضعیت آن — برای پیگیری و Verify.</summary>
    public class BnplPaymentRecord : BaseEntity
    {
        public int StoreId { get; set; }
        public int OrderId { get; set; }
        public int CustomerId { get; set; }
        public string ProviderKey { get; set; }

        /// <summary>شناسهٔ یکتای ما برای این تراکنش (providerId در دیجی‌پی / transactionId در اسنپ‌پی).</summary>
        public string TransactionId { get; set; }

        /// <summary>توکن/تیکت پرداخت برگشتی از ارائه‌دهنده.</summary>
        public string PaymentToken { get; set; }

        public decimal AmountToman { get; set; }

        public BnplPaymentStatus Status { get; set; }
        public string RawRequestJson { get; set; }
        public string RawResponseJson { get; set; }
        public DateTime CreatedOnUtc { get; set; }
        public DateTime? VerifiedOnUtc { get; set; }
    }

    public enum BnplPaymentStatus
    {
        Requested = 0,
        RedirectedToGateway = 10,
        Paid = 20,
        Failed = 30,
        Settled = 40,
        Reverted = 50
    }
}
