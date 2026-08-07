# NOPCOMMERCE-EXECUTION.md — سند فنی عملیاتی فاز اجرا روی nopCommerce

> این سند مکمل `ARCHITECTURE-NATIVE-v2.md` است (که تصمیم راهبردی «ادامه روی nopCommerce تا بلوغ
> برند IGBZ» را ثبت کرده). این‌جا جزئیات فنی روزمرهٔ همان تصمیم نگه‌داری می‌شود: وضعیت هر پلاگین،
> فهرست کامل باگ‌های یافت‌شده و رفع‌شده، و مرجع راستی‌آزمایی API در برابر سورس واقعی nopCommerce.

## ۱. پلاگین‌های فعال

| پلاگین | SystemName | نقش |
|---|---|---|
| `Nop.Plugin.Misc.MultiTenantStores` | `Misc.MultiTenantStores` | هستهٔ چندمستأجری: جداسازی Store، نگاشت دامنه، پلن/اشتراک، پرداخت، Integration Credentials |
| `Nop.Plugin.Misc.InstagramAssistant` | `Misc.InstagramAssistant` | دستیار اینستاگرام، کیف‌پول وفاداری، استودیوی عکس/ویدیوی AI |
| `Nop.Plugin.Misc.MasterSiteHub` | `Misc.MasterSiteHub` | داشبورد سایت مادر برای سوپرادمین پلتفرم |
| `Nop.Plugin.Api` | `Api` | Web API عمومی برای اپ‌های Flutter (Push، Deep Link) |

## ۲. راستی‌آزمایی مقابل سورس واقعی nopCommerce 4.90.6

نسخهٔ اول این پروژه (قبل از دریافت سورس واقعی) بر پایهٔ حدس API نوشته شده بود. پس از دریافت
سورس رسمی، موارد زیر بررسی و در صورت نیاز اصلاح شدند:

| API فرضی (نادرست) | واقعیت تایید‌شده در سورس | محل اصلاح |
|---|---|---|
| `IRepository<T>.TableAsync(...)` | وجود ندارد؛ باید از `Table` + `FirstOrDefaultAsync()` یا `GetAllAsync(...)` استفاده شود | `StoreDomainMappingService` (۳ مورد) |
| `ICustomerService.GetAttributeAsync/SaveAttributeAsync` | این متدها روی `IGenericAttributeService` هستند | `TenantAdminScopeFilter`, `CrossStoreCustomerGuardFilter`, `OrderPaidEventConsumer`, `MasterSiteAdminController`, `TenantProvisioningService` |
| `Customer.PhoneNumber` | فیلد واقعی `Customer.Phone` است | `OrderPaidEventConsumer` |
| `ICustomerService.GetCustomerByPhoneAsync` | وجود ندارد؛ باید از `GetAllCustomersAsync(phone: ...)` استفاده شود | `DeepLinkRoutingController` |
| `ICustomerService.GetCustomerByInstagramScopedIdAsync` | هرگز وجود نداشته (ساختگی بود)؛ IGSID باید به‌صورت GenericAttribute نگه‌داری و جست‌وجو شود | سرویس جدید `IInstagramCustomerLinkService` ساخته شد |
| `IProductService.SearchProductsAsync(..., publishedOnly: true)` | چنین پارامتری وجود ندارد (`showHidden`/`overridePublished` هست) | `MarketplaceOmnichannelService`, `SeoAndAdNetworksFeedService` |
| `IWorkflowMessageService.SendCampaignAsync` | این متد اصلاً روی این سرویس نیست (روی `ICampaignService` است و برای بازاریابی/کمپین است، نه اعلان تراکنشی) | `TenantPlanService` — با `IQueuedEmailService.InsertQueuedEmailAsync` واقعی جایگزین شد |
| `MultiTenantStoreContext` بدون متد Sync `GetCurrentStore()` | `IStoreContext` این متد را الزامی می‌کند | بازنویسی کامل با الگوی واقعی `WebStoreContext` |
| `Order.CustomerId` تنظیم‌نشده | فیلد الزامی (غیر nullable) که هرگز مقداردهی نمی‌شد | `TenantPlanService.CreateSubscriptionOrderAsync` امضایش عوض شد تا `customerId` بگیرد |
| `BaseNopEntityModel` به‌صورت `class` ارث‌بری شده بود | در ۴.۹۰ این یک `record` است؛ کلاس نمی‌تواند از رکورد ارث ببرد | `IntegrationCredentialModel` به `record` تبدیل شد |
| csproj با `Sdk="Microsoft.NET.Sdk.Razor"`، `TargetFramework=net8.0` | الگوی واقعی: `Sdk="Microsoft.NET.Sdk"`، `net9.0` (از `Directory.Build.props`)، `ProjectReference` فقط به `Nop.Web.Framework.csproj`، Content صریح برای هر `.cshtml` | هر ۴ فایل `.csproj` بازنویسی شد |
| `plugin.json` → `SystemName` با پیشوند `Nop.Plugin.` | باید بدون پیشوند باشد (مطابقت با پوشهٔ خروجی `Plugins/{SystemName}`) | هر ۴ فایل `plugin.json` اصلاح شد |
| `StandardPermissionProvider.ManagePlugins` | در ۴.۹۰ به `StandardPermission.Configuration.MANAGE_PLUGINS` (رشته) تغییر کرده | `IntegrationCredentialsController` |
| `[Area("Admin")]` (رشتهٔ خام) | ثابت واقعی `AreaNames.ADMIN` در `Nop.Web.Framework` | `IntegrationCredentialsController` |
| کلید HMAC Hardcode‌شده در `InstagramVipAutomationController` (نسخهٔ تکراری قدیمی) | باید از تنظیمات خوانده شود؛ توکن ویدیو باید از سرویس امضای واقعی (`ILmsAndVideoSecurityService`) بیاید | فایل تکراری حذف و نسخهٔ واحد بازنویسی شد |
| `IFcmService` بدون تولید Access Token واقعی | `Google.Apis.Auth` به‌صورت Transitive از طریق `Nop.Services` در دسترس است | پیاده‌سازی واقعی OAuth2 اضافه شد |

## ۳. موارد تکرار/ناهماهنگی بین پلاگین‌ها که یکی‌سازی شد

1. `ProductPhotoAiStudioService` — یک نسخهٔ کاملاً Fake (`return true` بدون پردازش) در
   `InstagramAssistant` وجود داشت، هم‌زمان با یک نسخهٔ واقعی و کامل (ولی به‌خاطر نبود
   `plugin.json`، در پلاگین نامعتبر `Nop.Plugin.MultiTenant.Core`). هر دو با هم ادغام شدند: پلاگین
   نامعتبر حذف، فایل‌های واقعی داخل `InstagramAssistant` منتقل شد.
2. `InstagramWalletDonationConsumer` و `InstagramVipAutomationController` — نسخه‌های کاملاً
   متفاوت و ناهماهنگ در دو پلاگین مختلف (`MultiTenantStores` و `InstagramAssistant`) وجود داشت.
   نسخهٔ قدیمی‌تر (که به سرویس‌های هرگز-تعریف‌نشده مثل `ITenantWalletService` ارجاع می‌داد و کلید
   HMAC را Hardcode کرده بود) حذف و نسخهٔ واحد اصلاح‌شده نگه داشته شد.
3. `ProductInsertedInstagramConsumer` — دو نسخهٔ کاملاً متفاوت. نسخهٔ `MultiTenantStores` به
   یک سرویس هرگز-تعریف‌نشده (`IInstagramContentPublishingService`) و دو فیلد/Navigation-Property
   ساختگی روی موجودیت واقعی nopCommerce (`Product.StoreId`، `Product.ProductPictures` — که در
   `Product.cs` واقعی اصلاً وجود ندارند) ارجاع می‌داد؛ کپشن پست را می‌ساخت ولی نسخهٔ
   `InstagramAssistant` آن را صرفاً می‌ساخت و دور می‌ریخت (`Task.CompletedTask`). نسخهٔ نهایی: کپشن
   واقعاً از طریق دو مرحلهٔ استاندارد Instagram Graph Content Publishing API (ساخت Container +
   Publish) با اعتبارنامهٔ واقعی از `ITenantIntegrationCredentialService` ارسال می‌شود.
4. `InstagramGrowthAcademyService` — کپی عیناً یکسان در هر دو پلاگین (فقط تفاوت Namespace)؛
   نسخهٔ تکراری حذف شد، نسخهٔ ثبت‌شده در `NopStartup` پلاگین `InstagramAssistant` باقی ماند.

**الگوی کلی این ۴ مورد:** هر جا یک قابلیت اینستاگرامی هم در `MultiTenantStores` (پلاگین زیرساخت
عمومی) و هم در `InstagramAssistant` (پلاگین تخصصی اینستاگرام) پیاده‌سازی شده بود، نسخهٔ
`MultiTenantStores` قدیمی‌تر/معیوب‌تر بود. برای هر قابلیت جدید اینستاگرامی، محل صحیح پیاده‌سازی
همیشه `InstagramAssistant` است.

## ۴. وضعیت پنل مدیریت اعتبارنامه (Integration Credentials)

مسیر: `/Admin/IntegrationCredentials/Index`. رمزنگاری با `IEncryptionService` واقعی nopCommerce.
دکمهٔ «تست اتصال» فقط در‌دسترس‌بودن سرور را می‌سنجد (نه صحت کامل کلید API) — این محدودیت عمداً در
پیام نتیجه ذکر می‌شود تا خودش تبدیل به یک «تیک جعلی موفقیت» نشود.

## ۵. باقی‌مانده‌های شناخته‌شده

فهرست کامل و به‌روز در `PLACEMENT-GUIDE.md` بخش ۶.
