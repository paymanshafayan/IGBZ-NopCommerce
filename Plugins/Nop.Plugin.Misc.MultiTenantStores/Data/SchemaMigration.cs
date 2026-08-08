namespace Nop.Plugin.Misc.MultiTenantStores.Data
{
    using FluentMigrator;
    using Nop.Data.Extensions;
    using Nop.Data.Migrations;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;

    [NopMigration("2025/01/01 09:00:00", "Nop.Plugin.Misc.MultiTenantStores base schema", MigrationProcessType.Installation)]
    public class SchemaMigration : Migration
    {
        public override void Up()
        {
            if (!Schema.Table(nameof(StoreDomainMapping)).Exists())
                Create.TableFor<StoreDomainMapping>();

            if (!Schema.Table(nameof(TenantPlan)).Exists())
                Create.TableFor<TenantPlan>();

            if (!Schema.Table(nameof(TenantStoreSubscription)).Exists())
                Create.TableFor<TenantStoreSubscription>();

            if (!Schema.Table(nameof(TenantIntegrationCredential)).Exists())
                Create.TableFor<TenantIntegrationCredential>();

            if (!Schema.Table(nameof(PaymentTransactionLedger)).Exists())
                Create.TableFor<PaymentTransactionLedger>();

            if (!Schema.Table(nameof(PendingMarketplaceSync)).Exists())
                Create.TableFor<PendingMarketplaceSync>();

            if (!Schema.Table(nameof(AffiliateReferralCode)).Exists())
                Create.TableFor<AffiliateReferralCode>();

            if (!Schema.Table(nameof(AffiliateCommissionLedger)).Exists())
                Create.TableFor<AffiliateCommissionLedger>();

            if (!Schema.Table(nameof(AffiliateWithdrawalRequest)).Exists())
                Create.TableFor<AffiliateWithdrawalRequest>();

            if (Schema.Table(nameof(AffiliateReferralCode)).Exists()
                && !Schema.Table(nameof(AffiliateReferralCode)).Index("IX_AffiliateReferralCode_Code").Exists())
            {
                Create.Index("IX_AffiliateReferralCode_Code")
                    .OnTable(nameof(AffiliateReferralCode))
                    .OnColumn(nameof(AffiliateReferralCode.Code)).Ascending()
                    .WithOptions().Unique();
            }

            if (!Schema.Table(nameof(CourseLesson)).Exists())
                Create.TableFor<CourseLesson>();

            if (!Schema.Table(nameof(CourseQuizQuestion)).Exists())
                Create.TableFor<CourseQuizQuestion>();

            if (!Schema.Table(nameof(CourseQuizOption)).Exists())
                Create.TableFor<CourseQuizOption>();

            if (!Schema.Table(nameof(CourseEnrollmentProgress)).Exists())
                Create.TableFor<CourseEnrollmentProgress>();

            if (!Schema.Table(nameof(CourseCertificate)).Exists())
                Create.TableFor<CourseCertificate>();

            if (!Schema.Table(nameof(WalletLedger)).Exists())
                Create.TableFor<WalletLedger>();

            // موجودی کیف‌پول همیشه از SUM(AmountToman) بر اساس (CustomerId, StoreId) محاسبه می‌شود —
            // بدون این ایندکس، این پرس‌وجو (که در هر کسر/واریز/نمایش موجودی اجرا می‌شود) با رشد
            // تعداد تراکنش‌ها به‌سرعت کند خواهد شد.
            if (Schema.Table(nameof(WalletLedger)).Exists()
                && !Schema.Table(nameof(WalletLedger)).Index("IX_WalletLedger_Customer_Store").Exists())
            {
                Create.Index("IX_WalletLedger_Customer_Store")
                    .OnTable(nameof(WalletLedger))
                    .OnColumn(nameof(WalletLedger.CustomerId)).Ascending()
                    .OnColumn(nameof(WalletLedger.StoreId)).Ascending();
            }

            // یک HostName نباید همزمان به دو فروشگاه نگاشت شود
            if (Schema.Table(nameof(StoreDomainMapping)).Exists()
                && !Schema.Table(nameof(StoreDomainMapping)).Index("IX_StoreDomainMapping_HostName").Exists())
            {
                Create.Index("IX_StoreDomainMapping_HostName")
                    .OnTable(nameof(StoreDomainMapping))
                    .OnColumn(nameof(StoreDomainMapping.HostName)).Ascending()
                    .WithOptions().Unique();
            }

            // قاعدهٔ الزامی بخش ۹.۲ سند معماری: TrackingNumber هرگز نباید دوبار با موفقیت تایید شود؛
            // این Unique Index تضمین سطح دیتابیس برای همان قاعده است (نه فقط منطق برنامه).
            if (Schema.Table(nameof(PaymentTransactionLedger)).Exists()
                && !Schema.Table(nameof(PaymentTransactionLedger)).Index("IX_PaymentTransactionLedger_TrackingNumber").Exists())
            {
                Create.Index("IX_PaymentTransactionLedger_TrackingNumber")
                    .OnTable(nameof(PaymentTransactionLedger))
                    .OnColumn(nameof(PaymentTransactionLedger.TrackingNumber)).Ascending()
                    .WithOptions().Unique();
            }

            if (Schema.Table(nameof(TenantIntegrationCredential)).Exists()
                && !Schema.Table(nameof(TenantIntegrationCredential)).Index("IX_TenantIntegrationCredential_Store_Provider").Exists())
            {
                Create.Index("IX_TenantIntegrationCredential_Store_Provider")
                    .OnTable(nameof(TenantIntegrationCredential))
                    .OnColumn(nameof(TenantIntegrationCredential.StoreId)).Ascending()
                    .OnColumn(nameof(TenantIntegrationCredential.ProviderKey)).Ascending();
            }

            // قاعدهٔ Idempotency مالی (بخش ۱۸.۶ سند معماری): کسر/واریز با همان
            // (CustomerId, StoreId, Reason, ReferenceCode) نباید دوبار ثبت شود. این Unique Index
            // تضمین سطح دیتابیس برای WalletService.TryDebitAsync است تا دو درخواست هم‌زمان (کلیک دوبل
            // یا Retry) نتوانند دو بار از کیف‌پول کسر کنند. (ReferenceCode خالی/null در SQL Server و
            // MySQL مجاز به تکرار است، پس فقط تراکنش‌های دارای شناسهٔ یکتا محدود می‌شوند.)
            if (Schema.Table(nameof(WalletLedger)).Exists()
                && !Schema.Table(nameof(WalletLedger)).Index("IX_WalletLedger_DebitIdempotency").Exists())
            {
                Create.Index("IX_WalletLedger_DebitIdempotency")
                    .OnTable(nameof(WalletLedger))
                    .OnColumn(nameof(WalletLedger.CustomerId)).Ascending()
                    .OnColumn(nameof(WalletLedger.StoreId)).Ascending()
                    .OnColumn(nameof(WalletLedger.Reason)).Ascending()
                    .OnColumn(nameof(WalletLedger.ReferenceCode)).Ascending()
                    .WithOptions().Unique();
            }

            // جلوگیری از ثبت کمیسیون تکراری روی همان سفارش برای همان معرف (حتی با دو Request هم‌زمان)
            if (Schema.Table(nameof(AffiliateCommissionLedger)).Exists()
                && !Schema.Table(nameof(AffiliateCommissionLedger)).Index("IX_AffiliateCommissionLedger_Order_Referrer").Exists())
            {
                Create.Index("IX_AffiliateCommissionLedger_Order_Referrer")
                    .OnTable(nameof(AffiliateCommissionLedger))
                    .OnColumn(nameof(AffiliateCommissionLedger.OrderId)).Ascending()
                    .OnColumn(nameof(AffiliateCommissionLedger.ReferrerCustomerId)).Ascending()
                    .WithOptions().Unique();
            }

            // جدول پایدار کدهای OTP (به‌جای IMemoryCache — با ری‌استارت/چند نمونه از بین نمی‌رود)
            if (!Schema.Table(nameof(PhoneOtpCode)).Exists())
                Create.TableFor<PhoneOtpCode>();

            if (Schema.Table(nameof(PhoneOtpCode)).Exists()
                && !Schema.Table(nameof(PhoneOtpCode)).Index("IX_PhoneOtpCode_Store_Phone").Exists())
            {
                Create.Index("IX_PhoneOtpCode_Store_Phone")
                    .OnTable(nameof(PhoneOtpCode))
                    .OnColumn(nameof(PhoneOtpCode.StoreId)).Ascending()
                    .OnColumn(nameof(PhoneOtpCode.PhoneNumber)).Ascending();
            }

            if (!Schema.Table(nameof(LandingContentBlock)).Exists())
                Create.TableFor<LandingContentBlock>();

            // --- دادهٔ اولیهٔ واقعی سایت مادر (نه دادهٔ فرضی؛ محتوای واقعی برای شروع، کاملاً از پنل
            // مدیریت قابل ویرایش/حذف طبق درخواست کاربر) — فقط یک‌بار در نصب اجرا می‌شود. ---
            SeedDefaultTenantPlans();
            SeedDefaultLandingContentBlocks();
        }

        private void SeedDefaultTenantPlans()
        {
            // ⚠️ LinkedProductId=0 موقتی است — باید بعد از نصب، از پنل مدیریت جدید (TenantPlansController)
            // به یک محصول واقعی nopCommerce (برای دریافت وجه) وصل شود؛ در زمان Migration چنین محصولی
            // هنوز وجود ندارد.
            Insert.IntoTable(nameof(TenantPlan)).Row(new
            {
                Name = "برنزی",
                SystemName = "bronze",
                Description = "شروع کار با فروشگاه اینستاگرامی: اپلیکیشن اختصاصی و وب‌سایت فروشگاهی کامل.",
                LinkedProductId = 0,
                MaxProductsAllowed = 200,
                MaxOrdersPerMonth = 500,
                AllowCustomDomain = true,
                AllowDedicatedApp = true,
                AllowStore = true,
                AllowInstagramAiAssistant = false,
                AllowInstagramAiAssistantPro = false,
                PriceMonthly = 490000m,
                PriceSixMonths = 2600000m,
                PriceYearly = 4900000m,
                TrialDurationDays = 0,
                DisplayOrder = 20,
                IsActive = true
            });

            Insert.IntoTable(nameof(TenantPlan)).Row(new
            {
                Name = "نقره‌ای",
                SystemName = "silver",
                Description = "همهٔ امکانات برنزی + دستیار هوشمند اینستاگرام برای تولید محتوا و پاسخ خودکار به کامنت‌ها.",
                LinkedProductId = 0,
                MaxProductsAllowed = 500,
                MaxOrdersPerMonth = 1500,
                AllowCustomDomain = true,
                AllowDedicatedApp = true,
                AllowStore = true,
                AllowInstagramAiAssistant = true,
                AllowInstagramAiAssistantPro = false,
                PriceMonthly = 890000m,
                PriceSixMonths = 4800000m,
                PriceYearly = 8900000m,
                TrialDurationDays = 0,
                DisplayOrder = 30,
                IsActive = true
            });

            Insert.IntoTable(nameof(TenantPlan)).Row(new
            {
                Name = "طلایی",
                SystemName = "gold",
                Description = "همهٔ امکانات نقره‌ای + دستیار اینستاگرام نسخهٔ Pro: مشتریان VIP برای ویدیوهای اشتراکی و حمایت مالی از طریق کامنت.",
                LinkedProductId = 0,
                MaxProductsAllowed = 0, // نامحدود
                MaxOrdersPerMonth = 0,  // نامحدود
                AllowCustomDomain = true,
                AllowDedicatedApp = true,
                AllowStore = true,
                AllowInstagramAiAssistant = true,
                AllowInstagramAiAssistantPro = true,
                PriceMonthly = 1490000m,
                PriceSixMonths = 8000000m,
                PriceYearly = 14900000m,
                TrialDurationDays = 0,
                DisplayOrder = 40,
                IsActive = true
            });

            Insert.IntoTable(nameof(TenantPlan)).Row(new
            {
                Name = "آزمایشی رایگان",
                SystemName = "trial",
                Description = "یک هفته امکانات کامل فروشگاه و اپلیکیشن را رایگان امتحان کنید.",
                LinkedProductId = 0,
                MaxProductsAllowed = 30,
                MaxOrdersPerMonth = 50,
                AllowCustomDomain = false,
                AllowDedicatedApp = true,
                AllowStore = true,
                AllowInstagramAiAssistant = false,
                AllowInstagramAiAssistantPro = false,
                PriceMonthly = 0m,
                PriceSixMonths = 0m,
                PriceYearly = 0m,
                TrialDurationDays = 7,
                DisplayOrder = 10,
                IsActive = true
            });
        }

        private void SeedDefaultLandingContentBlocks()
        {
            Insert.IntoTable(nameof(LandingContentBlock)).Row(new
            {
                PageKey = "store",
                MenuTitle = "فروشگاه",
                Title = "وب‌سایت فروشگاهی اختصاصی شما",
                SummaryText = "یک فروشگاه آنلاین کامل با دامنهٔ اختصاصی، درگاه پرداخت مستقیم و کیف‌پول، و ابزارهای هوش مصنوعی برای ساخت محتوای محصول — بدون نیاز به دانش فنی.",
                FeatureBulletsText = "دامنهٔ اختصاصی و SSL رایگان\nاستودیوی AI برای ساخت عکس و ویدیوی حرفه‌ای محصول\nپرداخت با کیف‌پول یا درگاه مستقیم بانکی\nباشگاه مشتریان با کش‌بک و کد تخفیف\nسیستم بازاریابی افیلیت با کد معرف اختصاصی\nاتصال خودکار به دیجی‌کالا، دیوار و ترب\nفروش دورهٔ آموزشی با ویدیوی محافظت‌شده",
                ImageUrl = "/images/features/store-placeholder.jpg",
                CtaText = "ادامه مطلب",
                DetailFullContent = "<p>فروشگاه اینستاگرامی شما یک وب‌سایت کامل و مستقل است، با همهٔ ابزارهایی که برای فروش حرفه‌ای لازم دارید.</p>",
                DetailImageUrlsText = "/images/features/store-detail-1.jpg\n/images/features/store-detail-2.jpg",
                DisplayOrder = 10,
                IsActive = true
            });

            Insert.IntoTable(nameof(LandingContentBlock)).Row(new
            {
                PageKey = "app",
                MenuTitle = "اپلیکیشن",
                Title = "اپلیکیشن اختصاصی Android و iOS",
                SummaryText = "همان فروشگاه شما، این‌بار به‌شکل یک اپلیکیشن واقعی با نام و آیکون اختصاصی خودتان روی گوشی مشتریانتان — تجربهٔ خریدی شبیه اسکرول‌کردن اینستاگرام.",
                FeatureBulletsText = "اپلیکیشن با نام و آیکون برند شما\nنسخهٔ Android و iOS\nاعلان‌های Push برای تخفیف و سفارش\nنمایش محصولات به‌سبک فید اینستاگرام\nورود سریع با شمارهٔ موبایل",
                ImageUrl = "/images/features/app-placeholder.jpg",
                CtaText = "ادامه مطلب",
                DetailFullContent = "<p>دیگر لازم نیست مشتریانتان مرورگر باز کنند — فروشگاه شما همیشه روی گوشی‌شان است.</p>",
                DetailImageUrlsText = "/images/features/app-detail-1.jpg\n/images/features/app-detail-2.jpg",
                DisplayOrder = 20,
                IsActive = true
            });

            Insert.IntoTable(nameof(LandingContentBlock)).Row(new
            {
                PageKey = "instagram-assistant",
                MenuTitle = "دستیار اینستاگرام",
                Title = "دستیار هوشمند اینستاگرام",
                SummaryText = "از لحظه‌ای که محصول جدید ثبت می‌کنید، دستیار هوشمند به‌جای شما پست می‌سازد، به کامنت‌ها پاسخ می‌دهد و لینک خرید می‌فرستد — و در نسخهٔ Pro، حتی از طرفداران‌تان حمایت مالی جمع می‌کند.",
                FeatureBulletsText = "انتشار خودکار پست برای هر محصول جدید با کد محصول روی تصویر\nپاسخ خودکار به کامنت + ارسال لینک خرید در دایرکت\nاستودیوی AI: حذف پس‌زمینه، ساخت مدل مجازی، تولید ویدیو\n(Pro) مشتریان VIP برای ویدیوهای اختصاصی با اشتراک ماهانه\n(Pro) دریافت حمایت مالی از طریق کامنت",
                ImageUrl = "/images/features/instagram-placeholder.jpg",
                CtaText = "ادامه مطلب",
                DetailFullContent = "<p>دستیار اینستاگرام، دستیار واقعی کسب‌وکار شماست: از تولید محتوا تا فروش، بدون این‌که شما پای گوشی بنشینید.</p>",
                DetailImageUrlsText = "/images/features/instagram-detail-1.jpg\n/images/features/instagram-detail-2.jpg",
                DisplayOrder = 30,
                IsActive = true
            });
        }

        public override void Down()
        {
            // در محیط Production حذف جدول به‌صورت خودکار توصیه نمی‌شود؛ عمداً خالی گذاشته شده.
        }
    }
}
