# HANDOFF — وضعیت پروژه IGBZ (برای ادامه در چت/Project جدید)

## ۱. تصمیم راهبردی فعلی
مهاجرت به پلتفرم بومی (.NET 9/MongoDB) **متوقف شده** تا زمان بلوغ تجاری برند IGBZ. توسعه فعلی
روی **nopCommerce 4.90.6 واقعی** (سورس رسمی در دسترس بود و کامل بررسی شد) ادامه دارد.

## ۲. ساختار پلاگین‌ها (۴ پلاگین، نه ۵)
- `Nop.Plugin.Misc.MultiTenantStores` — هستهٔ اصلی: چندمستأجری، دامنه، پرداخت (Parbad)، پلن/اشتراک،
  Integration Credentials، Affiliate Marketing، LMS (Course/Lesson/Quiz)، AI Usage Credit Wallet،
  همگام‌سازی مارکت‌پلیس (ترب/دیجی‌کالا/دیوار)، سئو/ترجمه با ذخیرهٔ واقعی
- `Nop.Plugin.Misc.InstagramAssistant` — دستیار اینستاگرام، استودیوی AI چندرسانه‌ای، کیف‌پول وفاداری
- `Nop.Plugin.Misc.MasterSiteHub` — داشبورد سوپرادمین پلتفرم
- `Nop.Plugin.Api` — Web API برای اپ‌های Flutter (Push، Deep Link)

(پلاگین پنجم `Nop.Plugin.MultiTenant.Core` که `plugin.json` نداشت، داخل `InstagramAssistant` ادغام شد.)

## ۳. اسناد مرجع (باید همیشه با هم خوانده شوند)
1. `ARCHITECTURE-NATIVE-v2.md` — معماری کامل (هم نسخهٔ بومی آینده، هم تصمیم فعلی nopCommerce)
2. `NOPCOMMERCE-EXECUTION.md` — جدول کامل راستی‌آزمایی API مقابل سورس واقعی + موارد تکراری رفع‌شده
3. `PLACEMENT-GUIDE.md` — راهنمای نصب روی nopCommerce واقعی + تنظیمات الزامی
4. `گزارش-ممیزی-مشکلات-یافت-شده.md` — فهرست ۱۹ باگ اولیهٔ ممیزی
5. `بررسی-تطابق-موارد-igbz.md` — تطابق ۱۴ ویژگی درخواستی با کد واقعی (قبل از آخرین دور کار)

## ۴. کاری که در آخرین دور (این چت) انجام شد
با اولویت‌بندی خودم روی خلأهای شناسایی‌شده در سند #۵:
1. **وصل‌کردن Endpointهای بلااستفاده:** `MarketplaceFeedsController` (فید ترب/یکتانت عمومی)،
   صف پس‌زمینه واقعی (`PendingMarketplaceSync` + `ProductChangedMarketplaceSyncConsumer` +
   `MarketplaceSyncScheduleTask`).
2. **باگ بحرانی تازه پیدا‌شده:** `SubscriptionExpiryScheduleTask` هرگز در دیتابیس ثبت نشده بود
   (فقط `IScheduleTask` را Implement می‌کرد) — یعنی هیچ‌وقت اجرا نمی‌شد. رفع شد؛
   `MarketplaceSyncScheduleTask` هم به همان روش ثبت شد.
3. **باگ امنیتی بحرانی تازه پیدا‌شده:** `TenantAdminScopeFilter` و `CrossStoreCustomerGuardFilter`
   (فیلترهای جداسازی امنیتی چندمستأجری) در DI ثبت شده بودند ولی **هرگز به هیچ Controller‌ای اعمال
   نشده بودند** — یعنی هیچ‌وقت واقعاً اجرا نمی‌شدند. اکنون `CrossStoreCustomerGuardFilter` به‌صورت
   Global Filter و `TenantAdminScopeFilter` روی هر Controller ادمین تننت با `[ServiceFilter]` اعمال شد.
4. **ترجمه/سئو:** `ProductAiToolsController` ساخته شد — حالا واقعاً در `LocalizedProperty` و روی
   خودِ `Product` ذخیره می‌شود (قبلاً فقط محاسبه می‌شد، هرگز Save نمی‌شد).
5. **Affiliate Marketing واقعی:** کد معرف کوتاه (`AffiliateReferralCode`)، دفترکل کمیسیون واقعی
   (`AffiliateCommissionLedger`)، Cookie Capture Filter، Consumer ثبت‌نام و Consumer پرداخت سفارش،
   API آمار برای کاربر (`/api/affiliate/my-stats`)، پنل ادمین تسویه‌حساب.
6. **LMS از صفر:** `CourseLesson`/`CourseQuizQuestion`/`CourseQuizOption`/`CourseEnrollmentProgress`/
   `CourseCertificate` + `CourseService` (کنترل دسترسی واقعی از روی سفارش پرداخت‌شده، نه Flag جعلی)
   + `CourseController` عمومی + پنل ادمین `CourseLessonsController` (فقط CRUD سرفصل؛ مدیریت سوالات
   آزمون هنوز UI ندارد — فقط Backend).
7. **کیف‌پول اعتبار AI مصرفی (نیازمندی #۱۲):** `AiUsageCreditLedger` + `AiUsageCreditService` +
   شارژ خودکار به‌ازای هر خرید (`AiCreditOrderBonusConsumer`) + شارژ نقدی واقعی از طریق همان درگاه
   Parbad موجود (`AiCreditWalletController`).
8. حذف یک فایل کاملاً جعلی جامانده از اولین دور ممیزی (`ApiManagementController.cs`).
9. افزودن «راهنمای دریافت API Key» per-provider به پنل Integration Credentials.

## ۵. تکمیل‌شده در دور بعدی (بعد از HANDOFF فوق)
1. **گیت مصرف اعتبار AI:** `AiMultimediaStudioController` جدید ساخته شد (پیش از این هیچ Controllerی
   `AiMultimediaStudioService` را صدا نمی‌زد). هر سه endpoint (عکس/ویدیو/صدا) قبل از فراخوانی AI
   بیرونی `TryDebitAsync` را صدا می‌زنند؛ در صورت شکست AI، اعتبار خودکار برمی‌گردد
   (`AiCreditReason.RefundFailedUsage` جدید).
2. **مدیریت سوالات آزمون:** `CourseQuizQuestionsController` + View ادمین کامل (افزودن/حذف سوال و
   گزینه، تعیین گزینهٔ صحیح). لینک متقابل با صفحهٔ `CourseLessons` اضافه شد.
3. **زیرساخت JWT:** `IJwtTokenService` در MultiTenantStores + Scheme `JwtBearer` افزوده‌شده به‌صورت
   افزایشی (بدون دست‌کاری کوکی پیش‌فرض ادمین/فروشگاه). طبق تصمیم خودِ `ARCHITECTURE-NATIVE-v2.md`
   (بند ۱۷۵: «tenantId از JWT Claim (موبایل)»).
4. **ورود با اینستاگرام (Business Login for Instagram):** `InstagramLoginController` —
   start/callback/me. Provider Key جدید `instagram.oauth` (App ID/Secret) جدا از `instagram.graph`
   (که از قبل یعنی توکن دسترسی صفحه/کسب‌وکار خودِ فروشگاه — این تناقض در حین کار پیدا و رفع شد).
   اولویت با App سطح پلتفرم (`InstagramAssistant:PlatformMetaAppId/Secret`) است، فقط اگر تننتی App
   اختصاصی ثبت کرده باشد همان اولویت می‌گیرد.
   ⚠️ محدودیت واقعی: فقط حساب‌های Business/Creator — حساب شخصی پشتیبانی نمی‌شود.
5. **ورود با شماره موبایل (OTP پیامکی) — جایگزین واقعی برای حساب‌های شخصی:**
   `IPhoneOtpAuthService` (کاوه‌نگار، Provider Key از قبل موجود ولی هرگز فراخوانی نمی‌شد) +
   `AuthController` عمومی (`Nop.Plugin.Api`) با سه endpoint:
   - `GET api/public/auth/login-options` — اپ فلاتر بر اساس این، دو دکمهٔ ورود (پیج تجاری / موبایل)
     را می‌سازد، نه Hardcode.
   - `POST api/public/auth/phone/request-otp`
   - `POST api/public/auth/phone/verify-otp`
6. **فالو+منشن استوری → کد تخفیف در دایرکت:** `InstagramFollowMentionRewardController` (Webhook
   واقعی متا با هندشیک تایید + امضای HMAC-SHA256) + `IInstagramFollowMentionRewardService` (بررسی
   واقعی فالو، صدور `Discount` یک‌بارمصرف واقعی nopCommerce، دفترکل `InstagramFollowMentionReward`
   با Unique Index ضد تکرار). شامل مسیریابی چندتننتی از یک Webhook مشترک
   (`ResolveStoreIdForBusinessAccountAsync`، با کش ۱۰دقیقه‌ای).
   **مسیر جایگزین برای شکست دایرکت:** اگر پیام دایرکت به‌خاطر پنجرهٔ ۲۴ساعتهٔ متا شکست بخورد، یک
   کامنت عمومی زیر همان پست/استوری گذاشته می‌شود که کاربر را به دایرکت‌دادن دعوت می‌کند (بدون درج
   کد واقعی در کامنت عمومی، تا کد توسط دیگران سوءاستفاده نشود). وضعیت در `FallbackCommentPosted` ثبت می‌شود.
7. **Digikala Variant ID mapping:** `MarketplaceSyncScheduleTask` دیگر از `product.Sku` استفاده
   نمی‌کند (که SKU داخلی فروشگاه را با شناسهٔ بیرونی دیجی‌کالا قاطی می‌کرد)؛ حالا از
   `IGenericAttributeService` با کلید `DigikalaVariantId` می‌خواند. چون قبلاً هیچ راهی برای *ثبت*
   این مقدار وجود نداشت، اکشن جدید `ProductAiToolsController.SaveDigikalaVariantId` هم اضافه شد.
8. **پرداخت سفارش با دو گزینه (کیف‌پول / درگاه مستقیم):** یافتهٔ مهم: هیچ‌کدام از این دو مسیر قبلاً
   به فرآیند واقعی «سفارش پرداخت‌شده» وصل نبودند — کیف‌پول (`CustomerWalletLedger`) فقط واریزی بود
   (بدون متد کسر)، و Parbad فقط برای شارژ کیف‌پول AI/اشتراک تننت استفاده می‌شد نه پرداخت سفارش.
   `OrderPaymentController` جدید (در InstagramAssistant؛ به‌خاطر وابستگی به سرویس کیف‌پول محلی —
   یادداشت بازآرایی برای فاز Native گذاشته شد) هر دو مسیر را با `MarkOrderAsPaidAsync` واقعی وصل
   می‌کند: `GET options` (موجودی کیف‌پول را برمی‌گرداند؛ اگر ناکافی باشد `wallet.available=false`)،
   `POST pay-with-wallet` (کسر Idempotent با `TryDebitForOrderPaymentAsync` جدید)، `POST
   pay-with-gateway` + `POST gateway-callback` (دقیقاً با انضباط تاییدِ واقعیِ همان الگوی
   AiCreditWalletController — نه فقط بازگشت کاربر از درگاه).
9. **⚠️ یکپارچه‌سازی کامل کیف‌پول (این آیتم بند ۸ را هم بازنویسی می‌کند):** به درخواست مستقیم کاربر،
   سه دفترکل جداگانه (اعتبار AI، کیف‌پول کش‌بک/حمایت مالی InstagramAssistant، و بخش موجودی
   Affiliate) در یک `WalletLedger` واحد در هستهٔ پلتفرم (MultiTenantStores) ادغام شدند:
   - `Domain/WalletLedger.cs` + `Services/WalletService.cs` (`IWalletService`) — تنها منبع موجودی
     برای همه‌چیز؛ شامل `RequestCashTopUpAsync`/`VerifyCashTopUpAsync` (شارژ نقدی واقعی از طریق
     Parbad، دقیقاً همان انضباط تایید که در پرداخت سفارش بود).
   - `AiUsageCreditLedger`/`IAiUsageCreditService` و `CustomerWalletLedger`/
     `ILoyaltyWalletAndInstagramContestService` **کاملاً حذف شدند** (چیزی Deploy نشده بود، پس
     نیازی به Migration انتقال داده نبود).
   - `AiMultimediaStudioController`: هزینه‌ها از واحد انتزاعی «اعتبار» به تومان واقعی تبدیل شدند
     (۳۰هزار/۷۵هزار/۱۵هزار تومان به‌ترتیب عکس/ویدیو/صدا — معادل نرخ قبلی).
   - `AffiliateMarketingService.ProcessOrderCommissionAsync`: کمیسیون حالا بلافاصله به کیف‌پول واحد
     واریز و قابل‌خرج می‌شود (نه فقط عددی در گزارش). `ApproveWithdrawalAsync` حالا واقعاً از کیف‌پول
     کسر می‌کند (امضا به `Task<bool>` تغییر کرد چون ممکن است موجودی از زمان درخواست کم شده باشد).
   - `OrderPaymentController` از InstagramAssistant به MultiTenantStores منتقل شد (چون کیف‌پولی که
     به آن وابسته بود حالا آن‌جاست) — یادداشت معماری قبلی در موردش دیگر منتفی است.
   - `AiCreditWalletController` حذف و با `WalletController` عمومی (`api/wallet/*`) جایگزین شد.
   - `InstagramWalletDonationConsumer` هم به‌روزرسانی شد؛ در همین حین یک باگ قدیمی (محتوای تکراری
     خارج از namespace که کامپایل را می‌شکست) هم پیدا و رفع شد، و پارامتر `commentId` برای
     Idempotency صحیح اضافه شد (این Consumer هنوز به هیچ Webhook‌ای وصل نیست — یک Orphan قدیمی).

## ۶. چیزی که هنوز باقی مانده (اولویت پیشنهادی بعدی)
- **شکل دقیق Payload وبهوک mentions متا** باید با یک تست واقعی از داشبورد متا تایید شود —
  `TryExtractMentioningUserId`/`TryExtractMediaId` چند مسیر محتمل را امتحان می‌کنند ولی راستی‌آزمایی نشده‌اند.
- **کلیدهای تنظیمات جدیدی که قبل از اجرا لازم‌اند** (User Secrets/appsettings، هیچ‌کدام Hardcode نشده‌اند):
  - `MultiTenantStores:JwtSigningSecret`
  - `InstagramAssistant:PlatformMetaAppId` / `InstagramAssistant:PlatformMetaAppSecret`
  - `InstagramAssistant:WebhookVerifyToken`
- **بدون NuGet Restore/dotnet build واقعی** — این محیط اینترنت ندارد؛ اولین Build واقعی روی سیستم
  کاربر باید انجام و خطاهای احتمالی گزارش شود. بخش‌های زیر بیشترین احتمال نیاز به اصلاح دارند چون
  در کدبیس نمونهٔ قبلی نداشتند: صدور Customer جدید (`InsertCustomerAsync`/`CustomerCustomerRoleMapping`
  در دو جا: InstagramLoginController و PhoneOtpAuthService)، و فیلدهای دقیق `Discount` در
  `InstagramFollowMentionRewardService`.
- **⚠️ سند `بررسی-تطابق-موارد-igbz.md` قدیمی/نامعتبر است** — بسیاری از مواردی که آن سند «ناقص»
  اعلام کرده بود (Affiliate Marketing، LMS، کیف‌پول AI مصرفی، فید دیجی‌کالا/ترب، سئو/ترجمهٔ ذخیره‌شده)
  در واقع از قبل به‌طور کامل و واقعی پیاده‌سازی شده بودند. قبل از استفادهٔ دوباره از آن سند برای
  اولویت‌بندی، وضعیت واقعی کد را مستقیم چک کنید، نه فقط آن فایل را.
- **nopCommerce IPaymentMethod استاندارد هنوز پیاده نشده** — پرداخت سفارش از طریق `OrderPaymentController`
  (کیف‌پول/Parbad) فقط برای اپ فلاتر (API) کار می‌کند. اگر Storefront استاندارد nopCommerce (نه فقط
  اپ) هم قرار است مورد استفاده باشد، به یک پیاده‌سازی واقعی `IPaymentMethod` نیاز است که در این
  کدبیس هیچ نمونه‌ای از آن وجود ندارد (ریسک بالا برای پیاده‌سازی کورکورانه بدون سورس nopCommerce).
- **موارد جزئی باقی‌مانده از سند اصلی:** افزودن موسیقی پس‌زمینه به پست AI (بند ۱ راهنما) و انتخاب
  برند-محور سرویس‌دهندهٔ AI داخلی به‌جای Endpoint نمادین (بند ۲ راهنما) — کم‌اهمیت‌تر از موارد بالا.

## ۷. تکمیل‌شده در دور بعدی
10. **موسیقی پس‌زمینهٔ ویدیوی AI (بند ۱ راهنما):** `IBackgroundMusicCatalogService` (فهرست ثابت
    Royalty-Free) + `GET api/instagram/ai-studio/background-music-tracks` + پارامتر
    `backgroundMusicTrackId` که تا به درخواست واقعی `Generate5SecProductVideoStoryAsync` پاس داده
    می‌شود. **⚠️ محدودیت واقعی:** چون Endpoint این سرویس هنوز نمادین است، معلوم نیست خودِ سرویس AI
    بیرونی (آتنا) اصلاً چنین قابلیتی دارد یا نام فیلد صحیح چیست — این پلمبینگ کد آماده است ولی به
    مستندات واقعی provider برای اتصال نهایی نیاز دارد (دقیقاً همان مسدودکنندهٔ بند ۲).
11. **بند ۲ (انتخاب برند-محور سرویس‌دهنده) بلاک‌شده ماند:** بدون مستندات واقعی API دیپ‌فا/آتنا/ویرا/
    ترجمیار/فرازین/میزبان‌بات (URL دقیق Endpoint، فرمت Request/Response، نحوهٔ احراز هویت)، امکان
    ندارد این Endpointهای نمادین را به‌طور معتبر به یک برند خاص وصل کرد — این کار نیاز به مستندات
    از کاربر دارد، نه فقط زمان بیشتر. **تصمیم کاربر (این دور):** فعلاً همین Endpointهای نمادین بماند؛
    وقتی مستندات واقعی در دسترس بود، این مورد دوباره باز شود.

## ۸. یافته‌های بازبینی کلی (General Review) این دور
- **باگ داده فرضی در `MasterSiteLandingController.GetLandingData` پیدا و رفع شد:** دقیقاً همان الگوی
  قبلاً رفع‌شده در MasterSiteAdminController (آواتار همیشه یک عکس ثابت، CategoryName بر اساس زوج/فرد
  اندیس، HasActiveStory همیشه true، ProductPreviewCount از فرمول ساختگی، TotalOrdersProcessed/
  PlatformUptime/AverageSetupTimeMinutes همه Hardcode) — حالا با دادهٔ واقعی (عکس/دستهٔ اولین محصول
  واقعی هر فروشگاه، شمارش واقعی سفارش‌ها) جایگزین یا (وقتی منبع واقعی نداشت) کاملاً حذف شد.
- **`Nop490CompatibilityChecker` (بررسی سازگاری امضای متدهای هستهٔ nopCommerce) از قبل نوشته شده بود
  ولی هیچ‌جا صدا زده نمی‌شد** — به `Configure()` در NopStartup پلاگین Api وصل شد تا واقعاً در
  Startup هشدار بدهد، نه این‌که فقط کد مرده بماند.
- **⚠️ چهار سرویس کامل و جدی که ساخته شده‌اند ولی هیچ Controllerی صداشان نمی‌زند (Orphan کامل،
  نه فقط یک متد):**
  1. `SnappPayBnplGateway` (`ISnappPayBnplGateway`) — اعتبارسنجی و اقساط خرید-بعداً-پرداخت اسنپ‌پی.
  2. `LogisticsAndShippingService` — ثبت مرسولهٔ پستی تیپاکس/تایپین.
  3. `GamificationAndAffiliateService` — چرخ‌وفلک جایزه، یادآوری پیامکی سبد رهاشده،
     **و یک `ProcessAffiliateCommissionAsync` جداگانه که با سیستم Affiliate یکپارچه‌شدهٔ فعلی
     (AffiliateMarketingService + کیف‌پول واحد) تداخل مفهومی دارد** — دو مسیر متفاوت برای «محاسبهٔ
     کمیسیون» در کدبیس وجود دارد؛ این یکی هرگز صدا زده نمی‌شود ولی باید قبل از هر توسعهٔ بعدی
     دربارهٔ Affiliate تصمیم گرفت که کدام مسیر درست است (پیشنهاد: این متد حذف یا با
     AffiliateMarketingService ادغام شود).
  4. `InstagramGrowthAcademyService` — محتوای آموزشی رشد (استراتژی‌ها و قالب‌های کمپین ویروسی).
  هیچ‌کدام Endpoint واقعی ندارند؛ قبل از ساختن Controller برایشان، باید اولویت‌بندی شوند (اولویت
  پیشنهادی بر اساس ارزش کسب‌وکار: BNPL و Logistics بالاتر از Gamification/Growth Academy).

## ۹. تکمیل‌شده در دور بعدی: اتصال هر ۴ سرویس Orphan + چند اصلاح جانبی
به ترتیب اولویت کسب‌وکاری، هر ۴ مورد بند ۸ وصل شدند:
1. **حل تناقض Affiliate:** متد تکراری و هرگز-صدا-زده‌نشدهٔ `ProcessAffiliateCommissionAsync` از
   `GamificationAndAffiliateService` حذف شد (فقط محاسبه می‌کرد و «صف‌بندی» می‌کرد، بدون واریز واقعی).
   مسیر واحد و درست همان `AffiliateMarketingService.ProcessOrderCommissionAsync` است.
2. **BNPL:** `BnplController` جدید (`api/checkout/bnpl/check-eligibility`) به `SnappPayBnplGateway` وصل شد.
3. **لجستیک:** `ShipmentController` ادمین جدید (`SuggestRoute` + `RegisterShipment`) به
   `LogisticsAndShippingService` وصل شد؛ آدرس واقعی گیرنده از `IAddressService` خوانده می‌شود.
4. **Gamification:** `GamificationController` (`api/gamification/spin-wheel`) ساخته شد؛
   `SpinWheelOfFortuneAsync` هم بازنویسی شد تا واقعاً یک Discount یک‌بارمصرف nopCommerce بسازد
   (قبلاً فقط یک رشتهٔ متنی بی‌مصرف تولید می‌کرد) و محدودیت یک‌بار در ۲۴ ساعت اضافه شد. همچنین
   `AbandonedCartRemindersController` ادمین جدید ساخته شد که پیامک واقعی از طریق کاوه‌نگار می‌فرستد
   (باگ قبلی «همیشه Customer=null پاس داده می‌شد» هم رفع شد).
5. **آکادمی رشد:** `InstagramGrowthAcademyController` (`GET strategies` / `GET campaign-templates`).
   **در همین حین یه باگ دادهٔ فرضی دیگه پیدا شد:** `ViralCampaignTemplate.ConvertedLeads` اعداد
   ثابت (۱۴۲۰ و ۳۸۹۰) به‌عنوان «تعداد لید واقعی تبدیل‌شده» نمایش می‌داد — دقیقاً همون الگوی قبلی؛
   چون هیچ سیستم شمارش واقعی‌ای پشتش نبود، این فیلد کاملاً حذف شد.

**اصلاح جانبی مهم (ریسک واقعی که در حین کار پیدا شد):** `PhoneOtpAuthService` مستقیماً
`customer.Phone = normalizedPhone` را Set می‌کرد، در حالی که `ICustomerService.GetAllCustomersAsync(phone: ...)`
(که همان سرویس برای پیدا کردن مشتری موجود استفاده می‌کند) طبق قرارداد nopCommerce از GenericAttribute
استاندارد (`NopCustomerDefaults.PhoneAttribute`) جست‌وجو می‌کند، نه یک ستون مستقیم. این یعنی مشتری
تازه‌ساخته‌شده در ورود بعدی پیدا نمی‌شد و دوباره ساخته می‌شد — رفع شد. **⚠️ همین ریسک در
`CourseController.cs` (کد از قبل موجود، نه نوشتهٔ من) هم هست (`customer.Phone` مستقیم خوانده
می‌شود) — هنوز رفع نشده، باید بعد از تایید ساختار واقعی Customer در nopCommerce 4.90.6 بررسی شود.

## ۱۰. یافته‌های تطبیق با تاریخچه چت اولیهٔ ساخت پروژه (igbz-ai-studio-chat.md)
کاربر تاریخچهٔ چتی که پروژه اولیه در آن طراحی شده بود را آپلود کرد. یافتهٔ کلیدی:

**⚠️ این چت در واقع مربوط به یک اپ شبیه‌ساز/دمو مبتنی بر React بوده (Google AI Studio)، نه یک
پروژهٔ واقعی nopCommerce.** «کدهای C#» در آن چت در واقع رشته‌متن‌هایی داخل فایل‌های
`csharpTemplates.ts`/`flutterTemplates.ts` بودند که برای Export/نمایش تولید می‌شدند — هرگز واقعاً
Compile یا اجرا نشدند. تمام ادعاهای «۱۰۰٪ کامل و بدون کد فیک» در آن چت به موفقیت Build همان اپ
React برمی‌گردد، نه nopCommerce واقعی. این دقیقاً توضیح می‌دهد چرا در طول این چت مکرراً به الگوی
«کد وجود دارد ولی هیچ Controllerی صداش نمی‌زند» یا «داده فرضی به‌جای واقعی» برخوردیم.

**مقایسه با کدبیس واقعی فعلی — این‌ها تایید شدند که واقعاً پیاده‌سازی شده‌اند** (نه فقط ادعا):
Parbad+BNPL، لجستیک تاپین، همگام‌سازی مارکت‌پلیس (دیجی‌کالا/دیوار/ترب)، سئو/شبکهٔ تبلیغات
(یکتانت/تپسل/تریبون)، کریپتو+ترجمه، LMS+امنیت ویدیو (واترمارک شامل IP هم هست)، کیف‌پول یکپارچه،
AI Studio، دستیار اینستاگرام.

**⚠️ گَپ جدی و تاییدشده که پیدا شد:** «قالب اختصاصی طرح اینستاگرام برای فروشگاه‌ها» (Grid محصولات
شبیه پست‌های اینستاگرام + نوار استوری بالای صفحه + پاپ‌آپ Reels/Feed هنگام کلیک روی محصول) — که در
چت اصلی صراحتاً و مکرراً «۱۰۰٪ تکمیل‌شده ✅» اعلام شده بود — **در کدبیس واقعی اصلاً وجود ندارد.**
`InstagramThemePlugin.cs` در پلاگین MasterSiteHub فقط یک اسکلت خالی `BasePlugin` است؛ هیچ فایل
View (.cshtml) واقعی برای Grid/Stories/Reels-Modal در کل پروژه پیدا نشد. این بزرگ‌ترین Gap
تاییدشده در این بازبینی است — یک تم کامل Storefront که باید از صفر ساخته شود.

**موارد کم‌اهمیت‌تر که در چت اصلی بودند ولی در کدبیس واقعی نیستند:**
- «سکه هدیه» (Gift Coin) به‌عنوان واحد واسط قبل از تبدیل به کیف‌پول — احتمالاً دیگر لازم نیست چون
  کیف‌پول یکپارچهٔ فعلی مستقیماً تومانی است؛ صرفاً یک لایهٔ نمایشی/گیمیفیکیشن بود.
- «راهنمای تصویری» (Modal راهنما با آیکون در هر بخش پنل ادمین) — این هم صرفاً یک قابلیت رابط
  کاربری در همان اپ شبیه‌ساز React بود (`AdminVisualGuideModal.tsx`)، نه چیزی که به Razor View
  واقعی nopCommerce نگاشت شود؛ اولویت پایین.
- لینک‌های واقعی App Store/Google Play/دانلود مستقیم APK — خارج از حوزهٔ این پکیج سرور (اپ فلاتر
  در این زیپ نیست).

## ۱۱. تکمیل‌شده در دور بعدی: پایپلاین «تولید محصول → پست → پاسخ خودکار کامنت»
کاربر ۴ نکته دربارهٔ این مسیر مطرح کرد؛ اولویت‌بندی و اجرا به شرح زیر:

1. **پایپلاین واقعی کامنت (بالاترین اولویت، جدی‌ترین نقص):**
   - `InstagramCommentAutomationController` جدید (`api/instagram/webhook/comments`) ساخته شد — Webhook
     واقعی متا (هندشیک تایید + امضای HMAC) که جایگزین `InstagramVipAutomationController.HandleCommentWebhook`
     شد (آن اکشن حذف شد؛ نه Webhook واقعی صداش می‌زد، نه دایرکت را واقعاً ارسال می‌کرد — فقط JSON
     توصیفی برمی‌گرداند).
   - تطبیق دقیق متن کامنت با `Product.Sku` واقعی فروشگاه اضافه شد (قبلاً هیچ تطبیقی وجود نداشت).
   - `IInstagramMessagingService` جدید (مشترک) اضافه شد: ارسال واقعی دایرکت، پاسخ عمومی به کامنت،
     و تلاش برای لایک کامنت (⚠️ لایک: endpoint واقعی متا برایش قطعی نیست، مستند شد).
   - الگوی حمایت مالی (`$عدد`) پیاده و **متصل** شد: صاحب واقعی فروشگاه از
     `TenantStoreSubscription.OwnerCustomerId` (از طریق `ITenantPlanService.GetSubscriptionByStoreIdAsync`)
     خوانده می‌شود. ⚠️ توجه: در نگارش اول این کنترلر، اشتباهاً ادعا شده بود که «هیچ نگاشت مالکیت
     فروشگاهی در پروژه وجود ندارد» و این مسیر عمداً غیرفعال گذاشته شده بود — این ادعا نادرست بود
     (بررسی ناقص انجام شده بود)؛ با بررسی دقیق‌تر این نگاشت پیدا و مسیر واقعاً وصل شد.
   - ⚠️ محدودیت شناخته‌شده: برای تطبیق SKU، کل کاتالوگ فروشگاه در هر کامنت لود می‌شود — برای
     کاتالوگ‌های خیلی بزرگ باید بعداً به یک ایندکس SKU→ProductId تبدیل شود.
2. **اتصال AI Studio به پست خودکار:** `ApplyDynamicSkuWatermarkAsync` از `ProductPhotoAiStudioService`
   استخراج شد (قبلاً واترمارک فقط داخل `ProcessProductPhotoAsync` قفل بود که همیشه حذف پس‌زمینهٔ
   هزینه‌بر AI را هم اجباری می‌کرد). `ProductInsertedInstagramConsumer` حالا قبل از پست، عکس را با
   این متد **رایگان و محلی** واترمارک می‌کند و به‌عنوان یک Picture جدید ذخیره می‌کند — دیگر عکس خام
   بدون کد محصول پست نمی‌شود. اگر واترمارک به هر دلیلی شکست بخورد، به تصویر خام برمی‌گردد (پست بدون
   واترمارک بهتر از عدم انتشار است).

**باقی‌مانده از این ۴ نکته (اولویت بعدی):**
3. گسترش ابزارهای تولید AI (مدل/آواتار انسانی با محصول، تولید کامل بدون عکس محصول).
4. ابزار کامل ویرایش دستی خروجی (برش/پاک‌کن/جابجایی متن) — کاربر خواستار عمق کامل شد؛ این عمدتاً
   یک قابلیت رابط کاربری تعاملی (Canvas) در اپ فلاتر است، نه فقط API — باید طراحی مشترک شود که چه
   بخشی سمت سرور (تولید/ذخیره) و چه بخشی سمت کلاینت (رسم/جابجایی) پیاده شود.

## ۱۲. تکمیل‌شده در دور بعدی: گسترش ابزارهای AI + ابزار کامل ویرایش دستی
3. **گسترش تولید AI:** دو متد جدید به `AiMultimediaStudioService` اضافه شد:
   `GenerateModelPhotoAsync`/`GenerateModelVideoAsync` — تولید عکس/ویدیوی مدل با محصول، با پارامتر
   `productImageUrl` اختیاری (اگر خالی باشد، تولید کاملاً از روی توضیح متنی انجام می‌شود، دقیقاً طبق
   نیازمندی کاربر). دو Endpoint جدید در `AiMultimediaStudioController`
   (`generate-model-photo` / `generate-model-video`) با هزینهٔ کیف‌پول (۴۵هزار/۹۵هزار تومان).
   ⚠️ مثل بقیهٔ AI Studio، Endpointها نمادین‌اند (تصمیم قبلی کاربر).
4. **ابزار کامل ویرایش دستی (برش/پاک‌کن/متن/قالب):** `IImageEditingService` + `ImageEditingController`
   (`api/instagram/ai-studio/edit/*`) جدید ساخته شد — با ImageSharp واقعی، نه شبیه‌سازی:
   - `crop` — برش با محدودسازی امن مختصات (بدون Exception روی ورودی نامعتبر)
   - `erase` — پاک‌کن واقعی مبتنی بر تصویر ماسک (سفید=پاک‌شود)، پیکسل‌به‌پیکسل روی کانال Alpha
   - `add-text` — افزودن متن در مختصات دلخواه با رنگ/سایز قابل‌تنظیم
   - `add-template` — مونتاژ قالب/فریم آماده روی تصویر
   نکتهٔ معماری مهم که مستند شد: بخش *تعاملی* (کشیدن قلم‌مو، درگ‌کردن متن با انگشت) ذاتاً باید سمت
   کلاینت (اپ فلاتر با Canvas واقعی) پیاده شود — این API فقط نتیجهٔ ساختاریافتهٔ آن تعامل (مختصات،
   تصویر ماسک) را می‌گیرد و به‌طور قابل‌اتکا روی تصویر واقعی اعمال می‌کند. هر عملیات نتیجه را
   به‌عنوان Picture جدید ذخیره و URL برمی‌گرداند تا بشود ویرایش‌ها را زنجیره کرد.
   ⚠️ یک API کم‌استفاده‌تر ImageSharp (`ProcessPixelRows` دو-تصویری) در `erase` استفاده شده — علامت‌گذاری شد که در صورت خطای Build جایگزین دارد.

## ۱۳. تکمیل‌شده در دور بعدی: قالب اختصاصی طرح اینستاگرام (بزرگ‌ترین Gap بند ۱۰)
⚠️ **تصمیم معماری مهم پیش از این کار:** سند `ARCHITECTURE-NATIVE-v2.md` صراحتاً Next.js را برای
Storefront نهایی تعیین کرده؛ به تایید صریح کاربر، این نسخه با Razor Views واقعی nopCommerce ساخته
شد **موقتاً برای همین فاز پلاگین**، تا زمان مهاجرت به Next.js. اگر/وقتی آن مهاجرت انجام شد، این
پلاگین (MasterSiteHub Views/Components) باید کنار گذاشته شود، نه این‌که با نسخهٔ Next.js دوباره‌کاری شود.

**چیزی که ساخته شد** (جایگزین `InstagramThemePlugin.cs` قدیمی که فقط یک BasePlugin خالی بدون هیچ
View واقعی بود — با وجود ادعای «۱۰۰٪ کامل» در چت طراحی اولیه):
- `InstagramThemePlugin` حالا `IWidgetPlugin` واقعی پیاده می‌کند و در Zone استاندارد `home_page_top`
  محتوا تزریق می‌کند (پایدارترین Zone که در تقریباً همهٔ نسخه‌های nopCommerce وجود دارد).
- `InstagramGridViewComponent` — داده‌های **واقعی** (محصولات واقعی همان فروشگاه، جدیدترین‌ها اول)،
  نه نمونهٔ ساختگی؛ شامل URL واقعی صفحهٔ محصول (از `IUrlRecordService.GetSeNameAsync`).
- `Views/Shared/Components/InstagramGrid/Default.cshtml`: نوار استوری (دایره‌ای، گرادیانت رنگی
  اینستاگرام) + Grid سه‌ستونه (سبک فید اینستاگرام) + Modal تمام‌صفحه («سبک Reels/Feed») که با کلیک
  روی هر محصول باز می‌شود و دکمهٔ «مشاهده و خرید» به صفحهٔ واقعی محصول لینک می‌دهد. داده‌های Modal
  از data-attribute های همان Tile خوانده می‌شود (بدون AJAX اضافه).
- نوار استوری از روی محصولات ساخته می‌شود، نه یک موجودیت جداگانهٔ «Story» — چون استوری واقعی
  اینستاگرام در خودِ اینستاگرام است، نه این پلتفرم (این تصمیم را باید مستند دانست، نه محدودیت).
- `.csproj` به‌روزرسانی شد (`_ViewImports.cshtml` + `Default.cshtml` به‌عنوان Content — طبق درسی که
  قبلاً یک‌بار در CourseQuizQuestions فراموش شده بود).
- ⚠️ عدم قطعیت مستندسازی‌شده: شکل دقیق `IWidgetPlugin` (بخصوص `HideInWidgetList`) باید با build
  واقعی این نسخهٔ nopCommerce تایید شود.

## ۱۴. تکمیل‌شده در دور بعدی: جداسازی سایت مادر (مقاومت در برابر فیلترینگ)
به درخواست کاربر، لندینگ/ثبت‌نام تننت از پلاگین به یک وب‌سایت **کاملاً مجزا** (Next.js، پوشهٔ جداگانه
`mother-site/`، خارج از این پکیج پلاگین) منتقل شد — هدف: اگر این سایت (محتوای عمومی/تبلیغاتی، ریسک
بیشتر برای فیلترشدن) فیلتر شد، فروشگاه‌های واقعی تننت روی زیرساخت اصلی از کار نیفتند. داشبورد
سوپرادمین (`MasterSiteAdminController`) طبق تصمیم کاربر همچنان داخل پلاگین باقی ماند.

**نکتهٔ مهم:** `MasterSiteLandingController` از قبل صرفاً یک API (`[ApiController]`, JSON) بود، نه
Razor View — پس چیزی برای «حذف» از منظر فرانت‌اند وجود نداشت؛ کاری که واقعاً لازم بود این بود که این
API را برای فراخوانی Cross-Origin از یک دامنهٔ کاملاً متفاوت آماده و کامل کنیم:

1. **باگ ریشه‌ای پیدا و رفع شد:** `TenantProvisioningService.ProvisionNewTenantStoreAsync` فقط برای
   مشتریان *از‌قبل‌موجود* کار می‌کرد — برای ثبت‌نام مستقیم (سناریوی عادی: مشتری کاملاً جدید)، هیچ
   Customer‌ای ساخته نمی‌شد، یعنی فروشگاه تازه‌ساز مالک نداشت. حالا اگر مشتری با آن ایمیل وجود
   نداشته باشد، حساب واقعی با رمز عبور واقعی (`ICustomerRegistrationService.RegisterAsync`) ساخته
   می‌شود. پارامتر `PlanId` هم قبلاً کاملاً نادیده گرفته می‌شد — رفع شد.
2. **Endpoint واقعی ثبت‌نام مستقیم:** `POST api/mastersite/public/signup` جدید در
   `MasterSiteLandingController` — ساخت حساب + فروشگاه + سفارش اشتراک واقعی
   (`TenantPlanService.CreateSubscriptionOrderAsync`) + لینک پرداخت واقعی (اگر پلن رایگان نباشد) +
   صدور JWT برای ورود خودکار. قبل از این، هیچ مسیر «ثبت‌نام مستقیم» واقعی در کل پروژه وجود نداشت.
3. **CORS واقعی:** Policy جدید `MasterSitePublicApi` (فعلاً `AllowAnyOrigin` برای شروع سریع — باید
   قبل از Production به دامنهٔ دقیق سایت مادر محدود شود) در `MultiTenantStoresPlugin.cs`.
4. **پروژهٔ Next.js واقعی ساخته شد** (`mother-site/`): صفحهٔ لندینگ (Hero + نوار استوری با فروشگاه‌های
   واقعی + پلن‌ها + آمار)، صفحهٔ ثبت‌نام (بررسی زندهٔ زیردامنه با Debounce، فرم کامل، مدیریت مسیر
   پرداخت)، و صفحهٔ `/signup/success` (تایید Callback پرداخت). ⚠️ این محیط اینترنت ندارد — کد نوشته
   شد ولی هرگز `npm install`/`npm run build` واقعی نشده؛ باید محلی تست شود. جزئیات کامل و کارهای
   باقی‌مانده در `mother-site/README.md`.

**سه مورد «باقی‌مانده» که در همین دور هم تکمیل شدند:**
- **عدم‌قطعیت پارامترهای Callback درگاه حل شد (نه فقط مستندسازی):** به‌جای حدس‌زدن نام پارامترهای
  URL که درگاه برمی‌گرداند، `Signup` حالا `trackingNumber` را هم در پاسخ برمی‌گرداند؛ سایت Next.js
  این مقدار و `orderId` را قبل از رفتن به درگاه در `localStorage` مرورگر ذخیره می‌کند و صفحهٔ
  `/signup/success` از همان‌جا می‌خواند — کاملاً مستقل از رفتار دقیق درگاه بانک.
- **Tailwind CSS واقعی اضافه شد:** تمام کامپوننت‌ها (Hero/Stories/Plans/Stats/فرم ثبت‌نام/صفحهٔ
  موفقیت) از Inline Style به کلاس‌های Tailwind با پالت رنگ برند (`tailwind.config.ts`) بازنویسی شدند.
- **CORS محدود شد:** به‌جای `AllowAnyOrigin` ثابت، کلید تنظیمات جدید
  `MultiTenantStores:MotherSiteOrigin` اضافه شد — اگر ست شود، فقط همان دامنه اجازهٔ فراخوانی دارد؛
  اگر ست نشود (مثلاً محیط توسعه)، به `AllowAnyOrigin` برمی‌گردد.

## ۱۵. تکمیل‌شده در دور بعدی: پلن‌های اشتراکی CMS-محور + محتوای سایت مادر + Gate کردن قابلیت‌ها
طبق ساختار دقیق درخواستی کاربر (برنزی=اپ+فروشگاه، نقره‌ای=+دستیار اینستا، طلایی=+دستیار Pro
[مشتریان VIP + حمایت مالی]، به‌علاوه یک پلن چهارم آزمایشی ۷روزه رایگان):

1. **`TenantPlan` گسترش یافت:** `PriceSixMonths`، `AllowInstagramAiAssistantPro`، `TrialDurationDays`،
   `DisplayOrder` اضافه شد. `BillingCycle` enum (Monthly/SixMonths/Yearly) جایگزین `bool isYearly`
   شد در همه‌جا (`TenantPlanService`، `MasterSiteLandingController`، `TenantBillingController`).
2. **CRUD واقعی پلن‌ها از پنل مدیریت:** `TenantPlansController` (Admin، فقط سوپرادمین) — قبلاً
   هیچ Insert/Update/Delete‌ای برای پلن‌ها وجود نداشت، فقط متدهای خواندنی.
3. **بلوک‌های محتوایی CMS-محور:** موجودیت جدید `LandingContentBlock` + `LandingContentBlocksController`
   (Admin CRUD کامل) — عنوان/خلاصه/ویژگی‌ها/تصویر/متن کامل صفحهٔ «ادامه مطلب» همگی از پنل مدیریت
   قابل درج/ویرایش/حذف، طبق درخواست صریح کاربر (نه Hardcode در Next.js).
4. **دادهٔ اولیهٔ واقعی Seed شد** (نه فرضی؛ محتوای واقعی شروع‌کار): ۴ پلن + ۳ بلوک محتوایی
   (فروشگاه/اپلیکیشن/دستیار اینستاگرام) مستقیماً در Migration درج می‌شوند. ⚠️ `LinkedProductId`
   پلن‌ها موقتاً ۰ است — باید بعد از نصب از پنل به محصول واقعی وصل شود.
5. **Endpointهای جدید عمومی:** `GET feature-blocks`، `GET feature-blocks/{pageKey}`.
6. **⚠️ Gate کردن واقعی قابلیت‌های Pro (نه فقط مستندسازی):**
   - `InstagramVipAutomationController.GetVipVideoToken`: قبل از صدور توکن ویدیوی VIP، چک می‌شود
     فروشگاه پلن طلایی (`AllowInstagramAiAssistantPro`) دارد یا نه.
   - `InstagramCommentAutomationController`: کل پاسخ خودکار کامنت نیاز به حداقل پلن نقره‌ای دارد؛
     الگوی حمایت مالی ($عدد) مشخصاً نیاز به پلن طلایی دارد.
   - `AiMultimediaStudioController`: تمام ۵ اکشن (Enhance/VideoStory/VoiceOver/GenerateModelPhoto/
     GenerateModelVideo) نیاز به حداقل پلن نقره‌ای دارند (استودیوی AI بخشی از دستیار اینستاگرام است).
7. **یافتهٔ جانبی مهم — `TenantBillingController` (کاملاً بررسی‌نشده تا این دور):** دقیقاً همان الگوی
   تکراری این پروژه را داشت — `GetSubscriptionStatus` وقتی اشتراک واقعی نبود، داده‌ی کاملاً ساختگی
   برمی‌گرداند (همیشه Trial، همیشه فعال، همیشه ۱۴ روز)؛ `CreateRenewalOrder` یک URL Placeholder
   (`/pay/gateway-redirect؟...`) برمی‌گرداند و هرگز واقعاً درگاه پرداخت را صدا نمی‌زد —
   `IPaymentService`/`IOrderProcessingService` تزریق شده بودند ولی هرگز استفاده نمی‌شدند. هر دو رفع
   شد؛ حالا از `IParbadPaymentService` واقعی استفاده می‌کند و صادقانه اعلام می‌کند وقتی اشتراکی نیست.
8. **فرانت‌اند Next.js کامل بازطراحی شد:**
   - `SiteHeader` با منوی داینامیک (از همان بلوک‌های محتوایی CMS، نه لیست ثابت) — دقیقاً
     «فروشگاه - اپلیکیشن - دستیار اینستا» طبق درخواست.
   - `HeroSection`: پیام «با هزینه‌ای کمتر از یک هاست ساده».
   - `FeatureShowcaseSection`: کادر تصویر-راست/متن-چپ + دکمهٔ «ادامه مطلب» → `/features/{pageKey}`
     (صفحهٔ اختصاصی کامل با گالری تصویر).
   - `PlansSection` + ویزارد ثبت‌نام: تاگل ماهانه/۶ماهه/یکساله، ۴ کارت پلن با نشان Pro (⭐)،
     نمایش ویژه برای پلن آزمایشی («X روز رایگان»، «بدون کارت بانکی»).
9. **⚠️ کار باقی‌مانده شناخته‌شده:** صفحات جدید ادمین (`TenantPlansController`/
   `LandingContentBlocksController`) فقط با لینک متقابل به‌هم وصل‌اند، ولی از منوی اصلی پنل ادمین
   nopCommerce قابل‌دسترسی نیستند (همان الگوی قبلاً مستندشده در HANDOFF برای CourseLessons) — نیاز
   به AdminMenu Override واقعی دارد.

## ۶. نکات مهم برای ادامه‌دهنده (چه انسان چه Claude جدید)
- همیشه قبل از نوشتن کد جدید، سورس واقعی nopCommerce (اگر در دسترس بود) را چک کن، نه حافظه/حدس.
- الگوی Ledger (SUM = موجودی) برای هر مقدار انباشتی مالی اجباری است؛ هرگز فیلد «موجودی فعلی» جدا نساز.
- هر عملیات مالی باید Idempotent باشد (شناسهٔ یکتا برای جلوگیری از تکرار).
- هیچ سرویس یکپارچه‌سازی نباید بدون فراخوانی HTTP واقعی «موفق» اعلام کند.
