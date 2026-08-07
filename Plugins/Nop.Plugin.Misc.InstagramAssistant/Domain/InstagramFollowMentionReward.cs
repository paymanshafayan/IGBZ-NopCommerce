namespace Nop.Plugin.Misc.InstagramAssistant.Domain
{
    using System;
    using Nop.Core;

    /// <summary>
    /// دفترکل پاداش‌های «فالو + منشن استوری → کد تخفیف در دایرکت». هر ردیف یعنی یک کد تخفیف واقعی
    /// nopCommerce (Discount با CouponCode یکتا) به یک کاربر اینستاگرام مشخص در یک فروشگاه مشخص
    /// صادر شده — Unique Index روی (StoreId, InstagramScopedId) تضمین می‌کند که این کار دوبار
    /// برای یک کاربر تکرار نشود.
    /// </summary>
    public class InstagramFollowMentionReward : BaseEntity
    {
        public int StoreId { get; set; }

        /// <summary>شناسهٔ Scoped کاربر اینستاگرام‌کننده (IGSID) — نه Username، چون Username می‌تواند تغییر کند.</summary>
        public string InstagramScopedId { get; set; }

        /// <summary>در صورت شناخته‌شدن (اگر کاربر قبلاً هم با اینستاگرام وارد شده)، شناسهٔ مشتری nopCommerce.</summary>
        public int? CustomerId { get; set; }

        public string CouponCode { get; set; }

        /// <summary>آیا ارسال پیام دایرکت حاوی کد موفق بود؟ (طبق سیاست پیام‌رسانی متا، فقط تا ۲۴ ساعت پس از آخرین تعامل کاربر با پیج امکان‌پذیر است.)</summary>
        public bool DirectMessageSent { get; set; }

        /// <summary>
        /// اگر دایرکت شکست خورد (معمولاً چون کاربر هرگز به پیج پیام نداده و پنجرهٔ ۲۴ساعته باز نیست)،
        /// به‌جایش یک کامنت عمومی زیر همان پست/استوری منشن‌شده گذاشته می‌شود که از کاربر می‌خواهد
        /// برای دریافت کد، به پیج دایرکت بدهد — کد واقعی هرگز در کامنت عمومی درج نمی‌شود.
        /// </summary>
        public bool FallbackCommentPosted { get; set; }

        public DateTime IssuedOnUtc { get; set; }
    }
}
