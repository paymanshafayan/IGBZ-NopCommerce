namespace Nop.Plugin.Misc.InstagramAssistant.Infrastructure
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Nop.Core.Infrastructure;
    using Nop.Plugin.Misc.InstagramAssistant.Consumers;
    using Nop.Plugin.Misc.InstagramAssistant.Services;

    public class NopStartup : INopStartup
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<IInstagramCustomerLinkService, InstagramCustomerLinkService>();
            services.AddScoped<IInstagramGrowthAcademyService, InstagramGrowthAcademyService>();
            services.AddScoped<IProductPhotoAiStudioService, ProductPhotoAiStudioService>();
            services.AddScoped<IAiVisionQualityService, AiVisionQualityService>();
            services.AddScoped<IAiBackgroundRemovalService, AiBackgroundRemovalService>();
            services.AddScoped<IAiMultimediaStudioService, AiMultimediaStudioService>();
            services.AddScoped<IInstagramFollowMentionRewardService, InstagramFollowMentionRewardService>();
            services.AddScoped<IBackgroundMusicCatalogService, BackgroundMusicCatalogService>();
            services.AddScoped<IInstagramMessagingService, InstagramMessagingService>();
            services.AddScoped<IImageEditingService, ImageEditingService>();

            // InstagramWalletDonationConsumer پیمانه IConsumer<T> استاندارد nopCommerce را پیاده
            // نمی‌کند (فراخوانی مستقیم از Webhook Controller اینستاگرام است)، پس باید صریحاً
            // به‌عنوان خودش ثبت شود، وگرنه Resolve نمی‌شود.
            services.AddScoped<InstagramWalletDonationConsumer>();

            // HttpClientهای نام‌گذاری‌شدهٔ مورد استفاده در AiMultimediaStudioService و
            // AiVisionAndBackgroundRemovalServices
            services.AddHttpClient("AiImageStudio");
            services.AddHttpClient("AiVideoStudio");
            services.AddHttpClient("AiTtsProvider");
            services.AddHttpClient("AiVisionQuality");
            services.AddHttpClient("AiBackgroundRemoval");
            services.AddHttpClient("InstagramGraphApi");
        }

        public void Configure(IApplicationBuilder application)
        {
        }

        public int Order => 11;
    }
}
