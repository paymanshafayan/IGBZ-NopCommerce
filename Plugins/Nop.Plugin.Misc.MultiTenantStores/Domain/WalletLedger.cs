namespace Nop.Plugin.Misc.MultiTenantStores.Domain
{
    using System;
    using Nop.Core;

    /// <summary>
    /// دفترکل واحد کیف‌پول مشتری — جایگزین سه دفترکل جداگانهٔ قبلی:
    /// <c>AiUsageCreditLedger</c> (اعتبار AI، واحد Credit)، <c>CustomerWalletLedger</c> در
    /// InstagramAssistant (کش‌بک/حمایت مالی/جایزه، واحد تومان)، و بخش «موجودی» جداگانهٔ
    /// AffiliateCommissionLedger. حالا همه‌چیز یک عدد واحد به تومان است و از همین یک جا برای
    /// «هر نوع پرداختی» (سفارش، شارژ مصرف AI، برداشت Affiliate) قابل استفاده است.
    /// موجودی واقعی همیشه از مجموع رکوردهای این جدول محاسبه می‌شود (مثبت = واریز، منفی = برداشت/مصرف).
    /// </summary>
    public class WalletLedger : BaseEntity
    {
        public int CustomerId { get; set; }
        public int StoreId { get; set; }
        public decimal AmountToman { get; set; }
        public WalletTransactionReason Reason { get; set; }
        public string ReferenceCode { get; set; }
        public DateTime CreatedOnUtc { get; set; }
    }

    public enum WalletTransactionReason
    {
        /// <summary>شارژ نقدی از طریق درگاه پرداخت واقعی (Parbad).</summary>
        CashTopUp = 0,

        /// <summary>کش‌بک درصدی روی خرید (در صورت فعال‌سازی طرح کش‌بک فروشگاه).</summary>
        OrderCashback = 10,

        /// <summary>پاداش اعتبار «دستیار هوشمند تولید محتوا» به‌ازای هر خرید (نیازمندی #۱۲).</summary>
        OrderAiFeatureBonus = 15,

        /// <summary>حمایت مالی دریافتی از طریق کامنت اینستاگرام (یا کسر از حساب حامی برای همین تراکنش).</summary>
        InstagramDonationReceived = 20,

        /// <summary>جایزهٔ مسابقهٔ اینستاگرامی.</summary>
        ContestReward = 30,

        /// <summary>کمیسیون واقعی کسب‌شده از طریق سیستم Affiliate — بلافاصله قابل‌خرج در همین کیف‌پول.</summary>
        AffiliateCommissionEarned = 40,

        /// <summary>خروج پول از کیف‌پول برای واریز بانکی برداشت Affiliate (پس از تایید ادمین).</summary>
        AffiliateWithdrawalToBank = 45,

        /// <summary>پرداخت مستقیم بهای سفارش از کیف‌پول.</summary>
        OrderPaymentDebit = 50,

        /// <summary>مصرف قابلیت‌های AI (تولید عکس/ویدیو/صدا).</summary>
        AiFeatureUsageDebit = 60,

        /// <summary>بازگشت خودکار اعتبار وقتی سرویس AI بیرونی پس از کسر موفق، در عمل شکست بخورد.</summary>
        AiFeatureUsageRefund = 65,

        ManualAdjustment = 100
    }
}
