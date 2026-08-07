namespace Nop.Plugin.Misc.MultiTenantStores.Domain
{
    using Nop.Core;

    /// <summary>
    /// بلوک‌های محتوایی صفحهٔ اصلی سایت مادر (معرفی فروشگاه/اپلیکیشن/دستیار اینستاگرام) — کاملاً از
    /// پنل مدیریت قابل درج/ویرایش/حذف، طبق درخواست صریح کاربر (نه Hardcode در فرانت‌اند).
    /// هر بلوک هم خلاصه‌ای برای نمایش در کادر صفحهٔ اصلی دارد و هم محتوای کامل برای صفحهٔ اختصاصی
    /// «ادامه مطلب».
    /// </summary>
    public class LandingContentBlock : BaseEntity
    {
        /// <summary>کلید صفحه — برای منوی سایت و مسیر URL استفاده می‌شود: مثلاً "store"، "app"، "instagram-assistant".</summary>
        public string PageKey { get; set; }

        public string MenuTitle { get; set; }
        public string Title { get; set; }

        /// <summary>متن خلاصه‌شده برای کادر صفحهٔ اصلی.</summary>
        public string SummaryText { get; set; }

        /// <summary>ویژگی‌های کلیدی — هر خط یک ویژگی (در پنل به‌صورت Textarea چندخطی وارد می‌شود).</summary>
        public string FeatureBulletsText { get; set; }

        public string ImageUrl { get; set; }
        public string CtaText { get; set; }

        /// <summary>محتوای کامل صفحهٔ اختصاصی («ادامه مطلب») — HTML ساده مجاز است.</summary>
        public string DetailFullContent { get; set; }

        /// <summary>آدرس عکس‌های صفحهٔ کامل — هر خط یک URL.</summary>
        public string DetailImageUrlsText { get; set; }

        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }
    }
}
