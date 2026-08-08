namespace Nop.Plugin.Misc.MultiTenantStores.Domain
{
    using System;
    using Nop.Core;

    /// <summary>
    /// وضعیت هر آیتم از چک‌لیست «فروشگاه‌ت رو بترکون» (راه‌اندازی/رشد فروشگاه) برای یک تننت.
    /// آیتم‌هایی که وضعیتشان به‌صورت خودکار از دادهٔ واقعی (مثل فعال‌بودن یک Integration Credential)
    /// قابل تشخیص است، در این جدول ذخیره نمی‌شوند — فقط آیتم‌های دستی که کاربر «انجام دادم/بعداً»
    /// می‌زند رکورد می‌گیرند.
    /// </summary>
    public class LaunchChecklistItemState : BaseEntity
    {
        public int StoreId { get; set; }
        public string ItemKey { get; set; }
        public LaunchChecklistStatus Status { get; set; }
        public DateTime? CompletedOnUtc { get; set; }
        public DateTime UpdatedOnUtc { get; set; }
    }

    public enum LaunchChecklistStatus
    {
        /// <summary>در انتظار — کاربر هنوز اقدامی نکرده.</summary>
        Pending = 0,

        /// <summary>کاربر «انجام دادم» را زده.</summary>
        Done = 10,

        /// <summary>کاربر «بعداً» را زده (فعلاً از لیست اصلی پنهان/کم‌اولویت می‌شود).</summary>
        Snoozed = 20
    }
}
