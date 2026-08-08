namespace Nop.Plugin.Misc.MasterSiteHub.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Nop.Services.Common;
    using Nop.Services.Plugins;
    using Nop.Services.Cms;
    using Nop.Plugin.Misc.MasterSiteHub.Components;

    /// <summary>
    /// «قالب اختصاصی طرح اینستاگرام» — نوار استوری + Grid محصولات به‌سبک پست اینستاگرام + Modal
    /// نمایش سریع هنگام کلیک روی محصول. طبق تصمیم صریح کاربر، این نسخه با Razor Views واقعی
    /// nopCommerce ساخته شده (موقتی برای همین فاز پلاگین، تا زمان مهاجرت به Next.js طبق
    /// ARCHITECTURE-NATIVE-v2.md). قبل از این تغییر، این کلاس فقط یک BasePlugin خالی بود — هیچ
    /// View یا IWidgetPlugin واقعی نداشت؛ یعنی با وجود ادعای «۱۰۰٪ تکمیل» در چت طراحی اولیه، هیچ
    /// خروجی بصری واقعی روی Storefront نمایش داده نمی‌شد.
    /// </summary>
    public class InstagramThemePlugin : BasePlugin, IMiscPlugin, IWidgetPlugin
    {
        public override async Task InstallAsync()
        {
            await base.InstallAsync();
        }

        public override async Task UninstallAsync()
        {
            await base.UninstallAsync();
        }

        public string GetConfigurationUrl()
        {
            return "/Admin/MasterSiteHub/ConfigureInstagramTheme";
        }

        public bool HideInWidgetList => false;

        /// <summary>
        /// home_page_top یکی از پایدارترین و رایج‌ترین Widget Zoneهای nopCommerce است (در تقریباً
        /// همهٔ نسخه‌ها وجود دارد) — انتخاب شد تا وابستگی به یک نسخهٔ خاص نداشته باشیم.
        /// </summary>
        public Task<IList<string>> GetWidgetZonesAsync()
        {
            return Task.FromResult<IList<string>>(new List<string> { "home_page_top" });
        }

        public Type GetWidgetViewComponent(string widgetZone)
        {
            return typeof(InstagramGridViewComponent);
        }
    }
}