namespace Nop.Plugin.Misc.MultiTenantStores
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Nop.Core;
    using Nop.Core.Infrastructure;
    using Nop.Core.Domain.ScheduleTasks;
    using Nop.Services.Plugins;
    using Nop.Services.Common;
    using Nop.Services.Localization;
    using Nop.Services.ScheduleTasks;
    using Nop.Plugin.Misc.MultiTenantStores.Services;
    using Nop.Plugin.Misc.MultiTenantStores.Infrastructure;
    using Nop.Plugin.Misc.MultiTenantStores.Infrastructure.Filters;

    public class MultiTenantStoresPlugin : BasePlugin, IMiscPlugin
    {
        private readonly ILocalizationService _localizationService;
        private readonly IScheduleTaskService _scheduleTaskService;

        public MultiTenantStoresPlugin(ILocalizationService localizationService, IScheduleTaskService scheduleTaskService)
        {
            _localizationService = localizationService;
            _scheduleTaskService = scheduleTaskService;
        }

        public override async Task InstallAsync()
        {
            await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
            {
                ["Plugins.Misc.MultiTenantStores.Credential.ProviderKey"] = "شناسهٔ Provider",
                ["Plugins.Misc.MultiTenantStores.Credential.ApiKey"] = "کلید API",
                ["Plugins.Misc.MultiTenantStores.Credential.ApiSecret"] = "راز API",
                ["Plugins.Misc.MultiTenantStores.Credential.EndpointOverrideUrl"] = "آدرس Endpoint اختصاصی",
                ["Plugins.Misc.MultiTenantStores.Credential.IsActive"] = "فعال",
                ["Plugins.Misc.MultiTenantStores.Credential.IsVerified"] = "تست‌شده (در دسترس)",
                ["Plugins.Misc.MultiTenantStores.Credential.LastTestedOnUtc"] = "آخرین تست اتصال",
                ["Plugins.Misc.MultiTenantStores.Credential.LastTestResultMessage"] = "نتیجهٔ آخرین تست"
            });

            // ثبت واقعی Schedule Taskها در دیتابیس — پیاده‌سازی IScheduleTask به‌تنهایی کافی نیست؛
            // بدون این رکورد، زمان‌بند ناپ‌کامرس هرگز این کلاس‌ها را فراخوانی نمی‌کند.
            if (await _scheduleTaskService.GetTaskByTypeAsync(SubscriptionExpiryTaskType) == null)
            {
                await _scheduleTaskService.InsertTaskAsync(new ScheduleTask
                {
                    Name = "بررسی انقضای اشتراک تننت‌ها",
                    Type = SubscriptionExpiryTaskType,
                    Enabled = true,
                    StopOnError = false,
                    Seconds = 24 * 60 * 60 // هر ۲۴ ساعت
                });
            }

            if (await _scheduleTaskService.GetTaskByTypeAsync(MarketplaceSyncTaskType) == null)
            {
                await _scheduleTaskService.InsertTaskAsync(new ScheduleTask
                {
                    Name = "همگام‌سازی پس‌زمینه با دیجی‌کالا/دیوار",
                    Type = MarketplaceSyncTaskType,
                    Enabled = true,
                    StopOnError = false,
                    Seconds = 5 * 60 // هر ۵ دقیقه، طبق پیشنهاد راهنمای اتصال مارکت‌پلیس
                });
            }

            // ساخت جداول دیتابیس با FluentMigrator در nopCommerce 4.90
            await base.InstallAsync();
        }

        public override async Task UninstallAsync()
        {
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Misc.MultiTenantStores.Credential");

            var subscriptionTask = await _scheduleTaskService.GetTaskByTypeAsync(SubscriptionExpiryTaskType);
            if (subscriptionTask != null) await _scheduleTaskService.DeleteTaskAsync(subscriptionTask);

            var marketplaceSyncTask = await _scheduleTaskService.GetTaskByTypeAsync(MarketplaceSyncTaskType);
            if (marketplaceSyncTask != null) await _scheduleTaskService.DeleteTaskAsync(marketplaceSyncTask);

            await base.UninstallAsync();
        }

        public string GetConfigurationUrl()
        {
            return "/Admin/IntegrationCredentials/Index";
        }

        private const string SubscriptionExpiryTaskType = "Nop.Plugin.Misc.MultiTenantStores.Tasks.SubscriptionExpiryScheduleTask";
        private const string MarketplaceSyncTaskType = "Nop.Plugin.Misc.MultiTenantStores.Tasks.MarketplaceSyncScheduleTask";
    }

    public class NopStartup : INopStartup
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            // ثبت سرویس‌های اصلی پلاگین MultiTenantStores
            services.AddScoped<IStoreDomainMappingService, StoreDomainMappingService>();
            services.AddScoped<ITenantProvisioningService, TenantProvisioningService>();
            services.AddScoped<ITenantPlanService, TenantPlanService>();
            services.AddScoped<ITenantIntegrationCredentialService, TenantIntegrationCredentialService>();
            services.AddScoped<IAffiliateMarketingService, AffiliateMarketingService>();
            services.AddScoped<ICourseService, CourseService>();
            services.AddScoped<IWalletService, WalletService>();
            services.AddScoped<ILandingContentBlockService, LandingContentBlockService>();
            services.AddScoped<IJwtTokenService, JwtTokenService>();
            services.AddScoped<IPhoneOtpAuthService, PhoneOtpAuthService>();
            services.AddMemoryCache();
            services.AddHttpClient("KavenegarSms");

            // به تصمیم کاربر برای جداسازی سایت مادر (لندینگ/ثبت‌نام) به یک وب‌سایت Next.js مجزا
            // (برای مقاومت در برابر فیلترینگ)، Endpointهای api/mastersite/public/* باید از یک دامنهٔ
            // کاملاً متفاوت قابل‌فراخوانی باشند. این Policy عمداً محدود به همان مسیر عمومی است، نه
            // کل API — سطح دسترسی گسترده به کل بک‌اند داده نمی‌شود.
            // دامنهٔ واقعی سایت مادر باید در تنظیمات (MultiTenantStores:MotherSiteOrigin) ست شود؛
            // اگر ست نشود، به AllowAnyOrigin بازمی‌گردیم (فقط برای توسعهٔ محلی مناسب است — قبل از
            // Production حتماً باید این مقدار تنظیم شود).
            var motherSiteOrigin = configuration["MultiTenantStores:MotherSiteOrigin"];
            services.AddCors(options =>
            {
                options.AddPolicy("MasterSitePublicApi", policyBuilder =>
                {
                    if (!string.IsNullOrWhiteSpace(motherSiteOrigin))
                        policyBuilder.WithOrigins(motherSiteOrigin).AllowAnyMethod().AllowAnyHeader();
                    else
                        policyBuilder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
                });
            });
            services.AddHttpClient("IntegrationCredentialTest");

            // افزودن Scheme احراز هویت JWT Bearer برای اپ موبایل فلاتر، به‌صورت افزایشی (Additive).
            // AddAuthentication() بدون آرگومان صرفاً AuthenticationBuilder موجود را برمی‌گرداند و
            // DefaultScheme کوکی‌ای که خودِ nopCommerce برای ادمین/فروشگاه ثبت کرده را بازنویسی
            // نمی‌کند — Controllerهای موبایل باید صریحاً با
            // [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] این
            // Scheme را درخواست کنند.
            services.AddAuthentication()
                .AddJwtBearer(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    var signingSecret = configuration["MultiTenantStores:JwtSigningSecret"];

                    // اگر کلید هنوز در تنظیمات ست نشده، برای بالا آمدن اپ یک کلید تصادفیِ فقط-حافظه
                    // (Ephemeral) تولید می‌شود — نه یک رازِ ثابتِ ناامن. نتیجه این است که هیچ توکنی
                    // (چون JwtTokenService با کلید خالی اصلاً Exception می‌دهد و توکنی صادر نمی‌شود)
                    // معتبر شناخته نخواهد شد؛ رفتار عمداً Fail-Closed است، نه یک پیش‌فرض باز و ناامن.
                    var effectiveKey = string.IsNullOrWhiteSpace(signingSecret)
                        ? Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
                        : signingSecret;

                    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                            System.Text.Encoding.UTF8.GetBytes(effectiveKey)),
                        ValidateIssuer = false,
                        ValidateAudience = false,
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.FromMinutes(2)
                    };
                });

            // سرویس‌های یکپارچه‌سازی بیرونی (بخش ۱۰ سند معماری) — همگی مبتنی بر HttpClient واقعی
            services.AddScoped<IParbadPaymentService, ParbadPaymentService>();
            services.AddScoped<ICryptoAndTranslationService, CryptoAndTranslationService>();
            services.AddScoped<IMarketplaceOmnichannelService, MarketplaceOmnichannelService>();
            services.AddScoped<ILogisticsAndShippingService, LogisticsAndShippingService>();
            services.AddScoped<IGamificationAndAffiliateService, GamificationAndAffiliateService>();
            services.AddScoped<ISeoAndAdNetworksFeedService, SeoAndAdNetworksFeedService>();

            // LmsAndVideoSecurityService به یک راز امضای HMAC نیاز دارد که هرگز نباید در appsettings
            // عمومی (Source Control) قرار گیرد — از User Secrets/Key Vault/Environment Variable خوانده شود.
            services.AddScoped<ILmsAndVideoSecurityService>(sp =>
            {
                var hmacSecret = configuration["MultiTenantStores:VodHmacSigningSecret"];
                if (string.IsNullOrWhiteSpace(hmacSecret))
                    throw new System.InvalidOperationException(
                        "کلید MultiTenantStores:VodHmacSigningSecret در تنظیمات پیدا نشد. این مقدار باید از " +
                        "User Secrets یا متغیر محیطی تنظیم شود، هرگز Hardcode نشود.");
                return new LmsAndVideoSecurityService(hmacSecret);
            });

            // SnappPayBnplGateway یک HttpClient اختصاصی می‌گیرد (Typed Client)
            services.AddHttpClient<SnappPayBnplGateway>();

            // HttpClientهای نام‌گذاری‌شدهٔ مورد استفاده در ParbadPaymentService/CryptoAndTranslationService/
            // MarketplaceOmnichannelService/LogisticsAndShippingService/SeoAndAdNetworksFeedService
            services.AddHttpClient("ParbadGateway");
            services.AddHttpClient("NowPayments");
            services.AddHttpClient("TranslationProvider");
            services.AddHttpClient("DigikalaOpenApi");
            services.AddHttpClient("KenarDivarApi");
            services.AddHttpClient("TapinPost");
            services.AddHttpClient("TriboonApi");

            // جایگزینی IStoreContext با پیاده‌سازی چندفروشگاهی واقعی
            services.AddScoped<IStoreContext, MultiTenantStoreContext>();

            // ثبت فیلترهای MVC برای جداسازی داده‌های تننت‌ها
            services.AddScoped<TenantAdminScopeFilter>();
            services.AddScoped<CrossStoreCustomerGuardFilter>();
            services.AddScoped<ReferralCookieCaptureFilter>();

            // نکتهٔ امنیتی مهم: ثبت این دو کلاس در DI به‌تنهایی کافی نیست — تا زمانی‌که به‌عنوان
            // فیلتر سراسری MVC اضافه نشوند، هرگز روی هیچ درخواستی اجرا نمی‌شوند (باگی که در ممیزی
            // این دور پیدا شد). با Configure<MvcOptions> این فیلترها را در سطح کل اپلیکیشن فعال
            // می‌کنیم، مستقل از این‌که AddControllersWithViews کجای Nop.Web فراخوانی شده باشد.
            services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(options =>
            {
                options.Filters.Add(new Microsoft.AspNetCore.Mvc.ServiceFilterAttribute(typeof(CrossStoreCustomerGuardFilter)));
                options.Filters.Add(new Microsoft.AspNetCore.Mvc.ServiceFilterAttribute(typeof(ReferralCookieCaptureFilter)));
                // TenantAdminScopeFilter عمداً سراسری نمی‌شود چون فقط باید روی Controllerهای Admin
                // چندمستأجری اعمال شود؛ باید صریحاً با [ServiceFilter(typeof(TenantAdminScopeFilter))]
                // روی هر Controller ادمین مرتبط اضافه شود.
            });
        }

        public void Configure(IApplicationBuilder application)
        {
            // فعال‌سازی CORS برای Endpointهای عمومی سایت مادر (بند بالا در ConfigureServices).
            application.UseCors("MasterSitePublicApi");
        }

        public int Order => 10;
    }
}