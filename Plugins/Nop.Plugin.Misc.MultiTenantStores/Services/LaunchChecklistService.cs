namespace Nop.Plugin.Misc.MultiTenantStores.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Nop.Data;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;

    /// <summary>
    /// چک‌لیست «فروشگاه‌ت رو بترکون» — راهنمای گام‌به‌گام راه‌اندازی/رشد فروشگاه برای هر تننت.
    /// برخی آیتم‌ها وضعیت خودکار دارند (از دادهٔ واقعی مثل فعال‌بودن Credential تشخیص داده می‌شوند)
    /// و برخی دستی‌اند (کاربر با دکمه «انجام دادم/بعداً» آن‌ها را مدیریت می‌کند).
    /// </summary>
    public interface ILaunchChecklistService
    {
        Task<IList<LaunchChecklistItemDto>> GetChecklistAsync(int storeId);
        Task<int> GetPendingCountAsync(int storeId);
        Task MarkDoneAsync(int storeId, string itemKey);
        Task MarkSnoozedAsync(int storeId, string itemKey);
    }

    public class LaunchChecklistItemDto
    {
        public string ItemKey { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string GuideUrl { get; set; }
        public string IconEmoji { get; set; }

        /// <summary>مراحل گام‌به‌گام راهنما (اختیاری — داخل خود چک‌لیست نمایش داده می‌شود).</summary>
        public IList<string> GuideSteps { get; set; }

        /// <summary>آیا وضعیت این آیتم خودکار تشخیص داده می‌شود یا دستی؟</summary>
        public bool IsAutoDetected { get; set; }

        /// <summary>توضیح وضعیت خودکار (مثلاً «اتصال فعال است» / «هنوز وصل نشده»).</summary>
        public string AutoStatusLabel { get; set; }

        /// <summary>
        /// مسیر داخلی پنل ادمین که ادمین فروشگاه باید برای تنظیم این مورد به آنجا برود
        /// (مثلاً بخش API Key/اتصالات). اگر null باشد، دکمهٔ «تنظیم در پنل» نمایش داده نمی‌شود.
        /// </summary>
        public string AdminActionUrl { get; set; }

        /// <summary>متن دکمهٔ داخلی (پیش‌فرض: «تنظیم در پنل»).</summary>
        public string AdminActionLabel { get; set; }

        public LaunchChecklistStatus Status { get; set; }
    }

    public class LaunchChecklistService : ILaunchChecklistService
    {
        private readonly IRepository<LaunchChecklistItemState> _stateRepository;
        private readonly ITenantIntegrationCredentialService _credentialService;
        private readonly IRepository<Domain.BnplCredential> _bnplCredentialRepository;

        public LaunchChecklistService(
            IRepository<LaunchChecklistItemState> stateRepository,
            ITenantIntegrationCredentialService credentialService,
            IRepository<Domain.BnplCredential> bnplCredentialRepository)
        {
            _stateRepository = stateRepository;
            _credentialService = credentialService;
            _bnplCredentialRepository = bnplCredentialRepository;
        }

        /// <summary>تعریف استاتیک آیتم‌ها — عنوان، توضیح، راهنما و نوع تشخیص.</summary>
        private static IList<LaunchChecklistItemDto> GetStaticItems() => new List<LaunchChecklistItemDto>
        {
            new()
            {
                ItemKey = "payir", Title = "درگاه پرداخت pay.ir",
                Description = "فعال‌سازی درگاه پرداخت ریالی برای دریافت وجه از مشتریان (الزامی برای فروش). کلید API را از «بخش اتصالات (API Key)» در پنل مدیریت تنظیم کنید.",
                GuideUrl = "https://pay.ir/", IconEmoji = "💳", IsAutoDetected = true,
                AutoStatusLabel = "درگاه pay.ir متصل است.",
                AdminActionUrl = "/Admin/IntegrationCredentials/Index",
                AdminActionLabel = "تنظیم کلید در اتصالات"
            },
            new()
            {
                ItemKey = "google_search_console", Title = "ثبت در گوگل سرچ کنسول",
                Description = "ثبت دامنهٔ فروشگاه در سرچ کنسول گوگل، تایید مالکیت و ارسال sitemap.xml تا صفحات سریع‌تر ایندکس شوند.",
                GuideUrl = "https://search.google.com/search-console", IconEmoji = "🔍", IsAutoDetected = false,
                AutoStatusLabel = null
            },
            new()
            {
                ItemKey = "google_indexing", Title = "ایندکس شدن در گوگل",
                Description = "درخواست ایندکس صفحات مهم (صفحهٔ اصلی، دسته‌ها و محصولات پرفروش) از سرچ کنسول. (نکته: API ایندکس گوگل فقط برای JobPosting/BroadcastEvent است — برای فروشگاه باید از sitemap + درخواست دستی استفاده شود.)",
                GuideUrl = "https://search.google.com/search-console", IconEmoji = "🚀", IsAutoDetected = false,
                AutoStatusLabel = null
            },
            new()
            {
                ItemKey = "google_business", Title = "گوگل بیزینس پروفایل",
                Description = "ثبت کسب‌وکار در گوگل مپ/بیزینس برای دیده‌شدن در جست‌وجوی محلی. (تایید پروفایل توسط خودِ صاحب‌کسب‌وکار انجام می‌شود.)",
                GuideUrl = "https://business.google.com", IconEmoji = "📍", IsAutoDetected = false,
                AutoStatusLabel = null
            },
            new()
            {
                ItemKey = "bing_webmaster", Title = "بینگ وب‌مستر",
                Description = "ثبت سایت در بینگ وب‌مستر + فعال‌سازی IndexNow برای ایندکس تقریباً آنی در بینگ/یاندکس.",
                GuideUrl = "https://www.bing.com/webmasters", IconEmoji = "🌐", IsAutoDetected = false,
                AutoStatusLabel = null
            },
            new()
            {
                ItemKey = "torob", Title = "ثبت در ترب (موتور جست‌وجوی قیمت)",
                Description = "ثبت فروشگاه در پنل فروشندگان ترب و معرفی فید محصولات تا محصولات در جست‌وجوی ترب نمایش داده شوند.",
                GuideUrl = "https://seller.torob.com", IconEmoji = "🛒", IsAutoDetected = false,
                AutoStatusLabel = null
            },
            new()
            {
                ItemKey = "emalls", Title = "ثبت در ایمالز",
                Description = "ثبت فروشگاه در ایمالز (موتور جست‌وجوی کالا) و اتصال فید محصولات برای افزایش فروش.",
                GuideUrl = "https://emalls.ir", IconEmoji = "🏷️", IsAutoDetected = false,
                AutoStatusLabel = null
            },
            new()
            {
                ItemKey = "instagram", Title = "اتصال دستیار اینستاگرام",
                Description = "وصل‌کردن توکن Instagram Graph API تا انتشار خودکار پست، پاسخ کامنت و دایرکت فعال شود. توکن را از «بخش اتصالات (API Key)» در پنل مدیریت تنظیم کنید.",
                GuideUrl = "https://developers.facebook.com/docs/instagram-api/", IconEmoji = "📸", IsAutoDetected = true,
                AutoStatusLabel = "دستیار اینستاگرام متصل است.",
                AdminActionUrl = "/Admin/IntegrationCredentials/Index",
                AdminActionLabel = "تنظیم کلید در اتصالات"
            },
            new()
            {
                ItemKey = "kavenegar", Title = "فعال‌سازی پیامک (کاوه‌نگار)",
                Description = "ثبت کلید API کاوه‌نگار برای ورود با کد یک‌بارمصرف و یادآوری سبد رهاشده. کلید را از «بخش اتصالات (API Key)» در پنل مدیریت تنظیم کنید.",
                GuideUrl = "https://kavenegar.com/rest.html", IconEmoji = "✉️", IsAutoDetected = true,
                AutoStatusLabel = "سرویس پیامک فعال است.",
                AdminActionUrl = "/Admin/IntegrationCredentials/Index",
                AdminActionLabel = "تنظیم کلید در اتصالات"
            },
            new()
            {
                ItemKey = "digipay", Title = "فعال‌سازی دیجی‌پی (خرید اعتباری/اقساطی)",
                Description = "اتصال دیجی‌پی BNPL تا مشتریان بتوانند «الان بخرند، بعداً پرداخت کنند» — افزایش چشمگیر فروش. اعتبارنامه را از «پنل پرداخت اعتباری (BNPL)» در پنل مدیریت تنظیم کنید.",
                GuideUrl = "https://www.mydigipay.com/bpg/", IconEmoji = "💎", IsAutoDetected = true,
                AutoStatusLabel = "دیجی‌پی متصل است.",
                AdminActionUrl = "/Admin/BnplAdmin/Index",
                AdminActionLabel = "تنظیم در پنل BNPL"
            },
            new()
            {
                ItemKey = "snapppay", Title = "فعال‌سازی اسنپ‌پی (خرید اقساطی)",
                Description = "اتصال اسنپ‌پی BNPL برای پرداخت اقساطی مشتریان. اعتبارنامه را از «پنل پرداخت اعتباری (BNPL)» در پنل مدیریت تنظیم کنید.",
                GuideUrl = "https://snapppay.ir/merchant-api-guide", IconEmoji = "🛵", IsAutoDetected = true,
                AutoStatusLabel = "اسنپ‌پی متصل است.",
                AdminActionUrl = "/Admin/BnplAdmin/Index",
                AdminActionLabel = "تنظیم در پنل BNPL"
            },
            new()
            {
                ItemKey = "virtual_tryon", Title = "پرو لباس با هوش مصنوعی",
                Description = "فعال‌سازی پرو مجازی لباس توسط مشتری (آپلود عکس خودش + انتخاب لباس) با مدل IDM-VTON — محلی/ابری. کلید/Endpoint را از «بخش اتصالات (API Key)» در پنل مدیریت تنظیم کنید. ⚠️ برای بهترین کیفیت: عکس کاربر با پس‌زمینهٔ ساده/تک‌رنگ و ایستادن صاف با دست‌های کمی باز؛ عکس لباس واضح و ترجیحاً تخت (Flat-lay).",
                GuideUrl = "https://github.com/yisol/IDM-VTON", IconEmoji = "👗", IsAutoDetected = true,
                AutoStatusLabel = "سرویس پرو لباس متصل است.",
                AdminActionUrl = "/Admin/IntegrationCredentials/Index",
                AdminActionLabel = "تنظیم کلید در اتصالات"
            },
            new()
            {
                ItemKey = "influencer", Title = "ساخت اینفلوئنسر اختصاصی برند",
                Description = "ارزش افزودهٔ اختیاری: طراحی چهرهٔ کاراکتر برند + تثبیت چهره و صدا با Google Flow و ساخت ویدیوهای تبلیغاتی اختصاصی.",
                GuideUrl = "https://flow.google.com", IconEmoji = "🎬", IsAutoDetected = false,
                AutoStatusLabel = null,
                GuideSteps = new List<string>
                {
                    "قدم اول (ساخت کاراکتر): یک چهرهٔ پایه که به برندتان بخورد طراحی کنید (با هوش مصنوعی) یا از پینترست پیدا کنید.",
                    "قدم دوم (تثبیت چهره): عکس را وارد ابزار Google Flow کنید و با یک پرامپت ساده، کاراکتر شیت (نماهای مختلف) بسازید تا چهره‌اش برای همیشه ثابت بماند؛ سپس صدای کاراکتر را انتخاب کنید.",
                    "قدم سوم (ورود به دفتر کار شما): عکس محیط کار و لوگوی برندتان را آپلود کنید و با ترکیبش با کاراکتر اختصاصی، یک ویدیوی تبلیغاتی فوق‌العاده بسازید.",
                    "ویدیوی نهایی را در اینستاگرام/ریلز منتشر کنید (دستی یا با کانکتور Manus).",
                    "هزینه: اشتراک Google/Flow (ممکن است برخی امکانات صدا نیاز به پلن Ultra داشته باشد) — بر عهدهٔ فروشنده."
                }
            },
            new()
            {
                ItemKey = "manus", Title = "اتصال Manus (تولید و انتشار محتوای هوشمند)",
                Description = "ارزش افزودهٔ اختیاری: با کانکتور اینستاگرام Manus، تصاویر/ریلز/کاروسل بسازید و مستقیم روی پیج منتشر کنید.",
                GuideUrl = "https://manus.im", IconEmoji = "🤖", IsAutoDetected = false,
                AutoStatusLabel = null,
                GuideSteps = new List<string>
                {
                    "در manus.im حساب بسازید و وارد شوید.",
                    "از تب Connectors، «Instagram» را انتخاب و با پیج تجاری/کریتور فروشگاه وصل کنید (OAuth).",
                    "یک Task بنویسید، مثلاً: «تصویر محصول X را با کپشن و هشتگ مناسب به‌صورت پست در اینستاگرام منتشر کن» — Manus انتشار را انجام می‌دهد.",
                    "برای تولید محتوا: «یک ریلز/کاروسل ۵ ثانیه‌ای برای محصول X بساز و منتشر کن» یا «یک گرید ۹تایی برای برندم بساز».",
                    "تحلیل: «آمار تعاملات (views/reach/likes/comments/saves) را بگیر و پیشنهاد استراتژی بده».",
                    "نکته: انتشار با کانکتور Manus بتاست؛ برای اتوماسیون قطعیِ رویدادمحور (مثل انتشار خودکار هنگام ثبت محصول)، دستیار اینستاگرام خودِ پلتفرم (Graph API) کار می‌کند.",
                    "هزینه: اشتراک Manus — بر عهدهٔ فروشنده."
                }
            },
            new()
            {
                ItemKey = "manychat", Title = "قیف کامنت → دایرکت با ManyChat",
                Description = "ارزش افزودهٔ اختیاری: راه‌اندازی قیف «کامنت کن تا لینک بفرستم» با ManyChat (نیازمند پلن Pro و اتصال به پیج اینستاگرام).",
                GuideUrl = "https://manychat.com", IconEmoji = "💬", IsAutoDetected = false,
                AutoStatusLabel = null,
                GuideSteps = new List<string>
                {
                    "در manychat.com ثبت‌نام کنید و پیج اینستاگرام (Business/Creator) را متصل کنید.",
                    "پلن Pro یا بالاتر فعال کنید (برای دسترسی کامل به API و اتصال‌ها؛ حدود ۱۵ دلار به بالا در ماه).",
                    "یک Flow جدید «Comment → DM» بسازید و تریگر را روی «کامنت شامل کلمهٔ کلیدی» تنظیم کنید.",
                    "پیام دایرکت خودکار را تنظیم کنید (لینک، کد تخفیف یا متن دلخواه).",
                    "برای اتصال به بک‌اند IGBZ (پردازش SKU/کیف‌پول/کد تخفیف): در فلوی ManyChat یک اکشن «HTTP Request» به آدرس وب‌هوک خودتان اضافه کنید.",
                    "در ریلزها بگویید: «کلمهٔ «رشد» را کامنت کن تا لینک رو بفرستم».",
                    "هزینه: اشتراک ManyChat (بر اساس تعداد مخاطب فعال) — بر عهدهٔ فروشنده."
                }
            }
        };

        public async Task<IList<LaunchChecklistItemDto>> GetChecklistAsync(int storeId)
        {
            var staticItems = GetStaticItems();
            var states = await _stateRepository.GetAllAsync(q => q.Where(s => s.StoreId == storeId));
            var stateByKey = states.ToDictionary(s => s.ItemKey, s => s, StringComparer.OrdinalIgnoreCase);

            // برای آیتم‌های خودکار، وضعیت از دادهٔ واقعی Credential ها تشخیص داده می‌شود
            var credentials = await _credentialService.GetByStoreIdAsync(storeId);
            var activeProviderKeys = new HashSet<string>(
                credentials.Where(c => c.IsActive).Select(c => c.ProviderKey),
                StringComparer.OrdinalIgnoreCase);

            // برای BNPL، وضعیت از جدول BnplCredential خوانده می‌شود
            var bnplCredentials = await _bnplCredentialRepository.GetAllAsync(q => q.Where(c => c.StoreId == storeId && c.IsActive));
            var activeBnplKeys = new HashSet<string>(
                bnplCredentials.Select(c => c.ProviderKey),
                StringComparer.OrdinalIgnoreCase);

            var result = new List<LaunchChecklistItemDto>();
            foreach (var item in staticItems)
            {
                var dto = new LaunchChecklistItemDto
                {
                    ItemKey = item.ItemKey,
                    Title = item.Title,
                    Description = item.Description,
                    GuideUrl = item.GuideUrl,
                    IconEmoji = item.IconEmoji,
                    GuideSteps = item.GuideSteps,
                    IsAutoDetected = item.IsAutoDetected,
                    AutoStatusLabel = item.AutoStatusLabel,
                    AdminActionUrl = item.AdminActionUrl,
                    AdminActionLabel = item.AdminActionLabel,
                    Status = LaunchChecklistStatus.Pending
                };

                if (item.IsAutoDetected)
                {
                    // تشخیص خودکار از روی ProviderKey فعال
                    var isActive = item.ItemKey switch
                    {
                        "payir" => activeProviderKeys.Contains("parbad.payir") || activeProviderKeys.Contains("payir"),
                        "instagram" => activeProviderKeys.Contains("instagram.graph"),
                        "kavenegar" => activeProviderKeys.Contains("kavenegar"),
                        "digipay" => activeProviderKeys.Contains("digipay") || activeProviderKeys.Contains("parbad.digipay") || activeBnplKeys.Contains("digipay"),
                        "snapppay" => activeProviderKeys.Contains("snapppay") || activeBnplKeys.Contains("snapppay"),
                        "virtual_tryon" => activeProviderKeys.Contains("virtual-tryon"),
                        _ => false
                    };

                    dto.Status = isActive ? LaunchChecklistStatus.Done : LaunchChecklistStatus.Pending;
                    dto.AutoStatusLabel = isActive
                        ? (item.AutoStatusLabel ?? "انجام شده است.")
                        : "هنوز وصل نشده است.";
                }
                else if (stateByKey.TryGetValue(item.ItemKey, out var state))
                {
                    dto.Status = state.Status;
                }

                result.Add(dto);
            }

            return result;
        }

        public async Task<int> GetPendingCountAsync(int storeId)
        {
            var items = await GetChecklistAsync(storeId);
            return items.Count(i => i.Status == LaunchChecklistStatus.Pending);
        }

        public async Task MarkDoneAsync(int storeId, string itemKey)
        {
            if (string.IsNullOrWhiteSpace(itemKey))
                return;

            var state = await GetOrCreateStateAsync(storeId, itemKey);
            state.Status = LaunchChecklistStatus.Done;
            state.CompletedOnUtc = DateTime.UtcNow;
            state.UpdatedOnUtc = DateTime.UtcNow;
            await _stateRepository.UpdateAsync(state);
        }

        public async Task MarkSnoozedAsync(int storeId, string itemKey)
        {
            if (string.IsNullOrWhiteSpace(itemKey))
                return;

            var state = await GetOrCreateStateAsync(storeId, itemKey);
            state.Status = LaunchChecklistStatus.Snoozed;
            state.UpdatedOnUtc = DateTime.UtcNow;
            await _stateRepository.UpdateAsync(state);
        }

        private async Task<LaunchChecklistItemState> GetOrCreateStateAsync(int storeId, string itemKey)
        {
            var existing = (await _stateRepository.GetAllAsync(q =>
                q.Where(s => s.StoreId == storeId && s.ItemKey == itemKey))).FirstOrDefault();

            if (existing != null)
                return existing;

            var created = new LaunchChecklistItemState
            {
                StoreId = storeId,
                ItemKey = itemKey,
                Status = LaunchChecklistStatus.Pending,
                UpdatedOnUtc = DateTime.UtcNow
            };
            await _stateRepository.InsertAsync(created);
            return created;
        }
    }
}
