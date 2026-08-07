# راهنمای نصب پلاگین‌ها روی nopCommerce 4.90.6 (تایید‌شده مقابل سورس واقعی)

## ۱. محل قرارگیری پلاگین‌ها
سورس nopCommerce 4.90.6 که آپلود کردید را به‌عنوان مبنا در نظر بگیرید. چهار پوشهٔ زیر را عیناً
داخل `src/Plugins/` (کنار سایر پلاگین‌های رسمی nopCommerce) کپی کنید:
- `Nop.Plugin.Misc.MultiTenantStores`
- `Nop.Plugin.Misc.InstagramAssistant`
- `Nop.Plugin.Misc.MasterSiteHub`
- `Nop.Plugin.Api`

(پوشهٔ پنجم `Nop.Plugin.MultiTenant.Core` وجود ندارد — چون اصلاً `plugin.json` نداشت، داخل
`InstagramAssistant` ادغام شد.)

## ۲. افزودن به Solution
هر ۴ پروژه را به `NopCommerce.sln` اضافه کنید:
```
dotnet sln src/NopCommerce.sln add src/Plugins/Nop.Plugin.Misc.MultiTenantStores/Nop.Plugin.Misc.MultiTenantStores.csproj
dotnet sln src/NopCommerce.sln add src/Plugins/Nop.Plugin.Misc.InstagramAssistant/Nop.Plugin.Misc.InstagramAssistant.csproj
dotnet sln src/NopCommerce.sln add src/Plugins/Nop.Plugin.Misc.MasterSiteHub/Nop.Plugin.Misc.MasterSiteHub.csproj
dotnet sln src/NopCommerce.sln add src/Plugins/Nop.Plugin.Api/Nop.Plugin.Api.csproj
```
ترتیب Build به‌خاطر `ProjectReference` بین پلاگین‌ها خودش مدیریت می‌شود (`MultiTenantStores` پایه است؛
سه پلاگین دیگر به آن ارجاع می‌دهند).

## ۳. تنظیمات الزامی قبل از اجرا (در `appsettings.json` یا ترجیحاً User Secrets)
بدون این مقادیر، سرویس‌های مربوطه در همان لحظهٔ Resolve شدن **عمداً** `InvalidOperationException`
پرتاب می‌کنند (نه موفقیت خاموش/جعلی):

```json
{
  "MultiTenantStores": {
    "VodHmacSigningSecret": "یک رشته تصادفی طولانی و امن، فقط برای این نصب"
  },
  "InstagramAssistant": {
    "VipLinkHmacSigningSecret": "یک رشته تصادفی طولانی و امن، جدا از بالا"
  },
  "Api": {
    "FcmProjectId": "شناسه پروژه Firebase شما",
    "FcmServiceAccountJsonPath": "مسیر مطلق فایل Service Account JSON گوگل روی سرور"
  }
}
```

## ۴. کلیدهای API درگاه‌ها و سرویس‌های بیرونی
از پنل ادمین: `/Admin/IntegrationCredentials/Index` (یا از لیست پلاگین‌ها → Configure مقابل
`Misc.MultiTenantStores`). آدرس‌های Endpoint نمادین (`api.parbad.local` و مشابه در کد) را با
آدرس واقعی هر PSP/API جایگزین کنید یا از طریق فیلد «آدرس Endpoint اختصاصی» در همان پنل ست کنید.

## ۵. آنچه در این دور، مقابل سورس واقعی nopCommerce 4.90.6 راستی‌آزمایی و اصلاح شد
فهرست کامل در `NOPCOMMERCE-EXECUTION.md` (بخش ممیزی)، خلاصه:
- امضای واقعی `IProductService.SearchProductsAsync` (بدون پارامتر `publishedOnly`)
- `GetAttributeAsync`/`SaveAttributeAsync` واقعاً روی `IGenericAttributeService` هستند، نه `ICustomerService`
- `Customer.Phone` (نه `PhoneNumber`)
- `IWorkflowMessageService.SendCampaignAsync` وجود نداشت؛ با `IQueuedEmailService` واقعی جایگزین شد
- `IRepository<T>.TableAsync` وجود نداشت (سه‌جا در `StoreDomainMappingService`)
- `MultiTenantStoreContext` متد Sync الزامی `IStoreContext.GetCurrentStore()` را نداشت
- الگوی واقعی `.csproj` پلاگین (SDK، OutputPath، ProjectReference به `Nop.Web.Framework`، Content
  صریح برای هر `.cshtml`)، `SystemName` در `plugin.json` (بدون پیشوند `Nop.Plugin.`)
- دو جفت فایل کاملاً تکراری/ناهماهنگ بین پلاگین‌ها (`InstagramWalletDonationConsumer`,
  `InstagramVipAutomationController`) که یکی‌شان کلید HMAC را Hardcode کرده بود — یکی‌سازی شد
- `Google.Apis.Auth` (که به‌صورت Transitive از طریق `Nop.Services` در دسترس است) برای صدور واقعی
  Access Token اعلان Push فعال شد (به‌جای پرتاب استثنای «هنوز پیاده نشده»)

## ۶. آنچه هنوز واقعاً ناقص است (صادقانه)
- **هیچ Unit/Integration Test** نوشته نشده.
- من همچنان نمی‌توانم `dotnet build` واقعی در این محیط اجرا کنم (بدون اینترنت برای NuGet Restore).
  با این‌حال این دور مقابل سورس واقعی nopCommerce راستی‌آزمایی شد، نه فقط حدس API — ریسک خطای
  Build اکنون بسیار پایین‌تر است، ولی صفر نیست (مثلاً نسخهٔ دقیق پکیج‌های NuGet مثل
  `SixLabors.ImageSharp` هنوز تقریبی است).
- Endpoint های واقعی PSP/مارکت‌پلیس/AI هنوز نمادین‌اند (بخش ۴ بالا).
- منطق تجاری ناقص شناخته‌شده: در ثبت‌نام تننت جدید، اگر ایمیل ادمین از قبل به‌عنوان مشتری در سایت
  مادر وجود نداشته باشد، حساب جدید ساخته نمی‌شود (فقط حساب موجود آپدیت می‌شود) — باید تکمیل شود.

اگر در Build واقعی به خطا برخوردید، پیام خطا را برایم بفرستید؛ با توجه به سطح راستی‌آزمایی این دور،
بیشتر خطاهای احتمالی باقی‌مانده جزئی (نسخهٔ پکیج، namespace فراموش‌شده) خواهند بود، نه اشتباهات
ساختاری بزرگ.
