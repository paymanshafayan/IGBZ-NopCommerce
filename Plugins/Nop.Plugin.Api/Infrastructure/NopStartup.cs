namespace Nop.Plugin.Api.Infrastructure
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Nop.Core.Infrastructure;
    using Nop.Plugin.Api.Services;

    public class NopStartup : INopStartup
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddHttpClient("FcmV1");

            services.AddScoped<IFcmService>(sp =>
            {
                var projectId = configuration["Api:FcmProjectId"];
                if (string.IsNullOrWhiteSpace(projectId))
                    throw new InvalidOperationException(
                        "کلید Api:FcmProjectId در تنظیمات پیدا نشد. شناسه پروژه Firebase باید تنظیم شود.");

                var serviceAccountJsonPath = configuration["Api:FcmServiceAccountJsonPath"];
                if (string.IsNullOrWhiteSpace(serviceAccountJsonPath))
                    throw new InvalidOperationException(
                        "کلید Api:FcmServiceAccountJsonPath در تنظیمات پیدا نشد. مسیر فایل Service " +
                        "Account گوگل (JSON) باید تنظیم شود تا Push Notification واقعی کار کند.");

                var httpClientFactory = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>();
                var deviceTokenRepository = sp.GetRequiredService<Nop.Data.IRepository<AdminDeviceToken>>();

                // تولید واقعی Access Token با Google.Apis.Auth (که از طریق Nop.Services به‌صورت
                // Transitive در دسترس است) — به‌جای پرتاب استثنا یا مقدار ثابت جعلی.
                Func<Task<string>> oauthTokenProvider = async () =>
                {
                    var credential = Google.Apis.Auth.OAuth2.GoogleCredential
                        .FromFile(serviceAccountJsonPath)
                        .CreateScoped("https://www.googleapis.com/auth/firebase.messaging");

                    if (credential.UnderlyingCredential is not Google.Apis.Auth.OAuth2.ServiceAccountCredential serviceAccountCredential)
                        throw new InvalidOperationException("فایل Service Account معتبر Google شناسایی نشد.");

                    return await serviceAccountCredential.GetAccessTokenForRequestAsync();
                };

                return new FcmService(deviceTokenRepository, httpClientFactory, projectId, oauthTokenProvider);
            });
        }

        public void Configure(IApplicationBuilder application)
        {
            // این بررسی از قبل نوشته شده بود ولی هیچ‌جا صدا زده نمی‌شد — یعنی اگر نسخهٔ nopCommerce
            // تغییر می‌کرد و امضای این متدهای کلیدی می‌شکست، بدون هیچ هشداری فقط در زمان اجرا خطای
            // مبهم می‌گرفتیم. حالا در همان لحظهٔ Startup اپ، یک هشدار واضح در لاگ ثبت می‌شود.
            var report = Nop490CompatibilityChecker.VerifyCoreSignatures();
            if (!report.IsAllValid)
            {
                var logger = application.ApplicationServices.GetService(typeof(Microsoft.Extensions.Logging.ILogger<NopStartup>))
                    as Microsoft.Extensions.Logging.ILogger<NopStartup>;
                logger?.LogWarning(
                    "ناسازگاری نسخهٔ nopCommerce شناسایی شد (هدف: {TargetVersion}, .NET: {FrameworkVersion}). " +
                    "OrderService={OrderServiceValid} StoreMapping={StoreMappingValid} CategoryService={CategoryServiceValid}. " +
                    "احتمالاً امضای یکی از این سرویس‌ها در نسخهٔ فعلی nopCommerce تغییر کرده است.",
                    report.TargetNopVersion, report.FrameworkVersion,
                    report.OrderServiceValid, report.StoreMappingValid, report.CategoryServiceValid);
            }
        }

        public int Order => 12;
    }
}
