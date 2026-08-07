namespace Nop.Plugin.Misc.InstagramAssistant.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    public interface IInstagramGrowthAcademyService
    {
        Task<List<GrowthStrategyGuide>> GetGrowthStrategiesAsync();
        Task<List<ViralCampaignTemplate>> GetViralCampaignTemplatesAsync();
    }

    public class InstagramGrowthAcademyService : IInstagramGrowthAcademyService
    {
        public async Task<List<GrowthStrategyGuide>> GetGrowthStrategiesAsync()
        {
            return await Task.FromResult(new List<GrowthStrategyGuide>
            {
                new GrowthStrategyGuide
                {
                    Title = "قلاب‌های وایرال (Call To Action) در ریلز",
                    Description = "نحوه دعوت کاربر به گذاشتن کامنت کلیدواژه جهت فعال‌شدن ربات دایرکت و افزایش ۵ برابری اینگیجمنت ریلز.",
                    ImpactScore = 98,
                    Category = "تکنیک‌های تعامل"
                },
                new GrowthStrategyGuide
                {
                    Title = "قانون ۲۴ ساعته Meta و جلوگیری از بلاک شدن",
                    Description = "مدیریت زمان‌بندی ارسال دایرکت، صف‌بندی (Queue) ۲ ثانیه‌ای و استفاده از دکمه‌های Quick Reply شیشه‌ای.",
                    ImpactScore = 95,
                    Category = "امنیت و الگوریتم"
                },
                new GrowthStrategyGuide
                {
                    Title = "تبدیل فالور رایگان به مشتری VIP با واترمارک پویا",
                    Description = "ارائه نمونه ویدئوی رایگان در In-App Webview با واترمارک متحرک نام کاربر و سپس هدایت به خرید دوره اصلی.",
                    ImpactScore = 92,
                    Category = "فروش و نرخ تبدیل"
                }
            });
        }

        public async Task<List<ViralCampaignTemplate>> GetViralCampaignTemplatesAsync()
        {
            // ⚠️ نسخهٔ قبلی این متد یک فیلد ConvertedLeads داشت که مقادیر ثابت (۱۴۲۰ و ۳۸۹۰) را به‌عنوان
            // «تعداد لید تبدیل‌شدهٔ واقعی» نمایش می‌داد — دقیقاً همان الگوی «داده فرضی به‌جای واقعی»ی
            // که در MasterSiteAdminController/MasterSiteLandingController پیدا و رفع شد. این
            // پلتفرم هیچ سیستمی برای شمارش واقعی لید تبدیل‌شده به‌ازای هر قالب کمپین ندارد؛ نمایش آن
            // عدد به کاربر گمراه‌کننده بود. اگر در آینده چنین شماره‌ای لازم شد، باید از رویدادهای
            // واقعی (مثلاً تعداد پیام‌های دایرکت ارسال‌شده با همین Keyword) محاسبه شود، نه Hardcode.
            return await Task.FromResult(new List<ViralCampaignTemplate>
            {
                new ViralCampaignTemplate
                {
                    Keyword = "VIP",
                    TargetProduct = "دوره جامع مدرسین فوق‌حرفه‌ای",
                    AutoReplyText = "سلام! برای دریافت جزئیات و دانلود ویدئوی نمونه با واترمارک پویا، روی دکمه زیر کلیک کنید."
                },
                new ViralCampaignTemplate
                {
                    Keyword = "هدیه",
                    TargetProduct = "جزوه رایگان + فایل صوتی تمرکزی",
                    AutoReplyText = "هدیه شما آماده است! روی لینک کلیک کنید تا در مرورگر داخلی اینستاگرام باز شود."
                }
            });
        }
    }

    public class GrowthStrategyGuide
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public int ImpactScore { get; set; }
        public string Category { get; set; }
    }

    public class ViralCampaignTemplate
    {
        public string Keyword { get; set; }
        public string TargetProduct { get; set; }
        public string AutoReplyText { get; set; }
    }
}