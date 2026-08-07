namespace Nop.Plugin.Misc.InstagramAssistant.Controllers
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Plugin.Misc.InstagramAssistant.Services;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// نقطهٔ ورود واقعی HTTP برای «استودیوی AI چندرسانه‌ای» (نیازمندی #۱۲ فهرست ویژگی‌ها).
    /// تا قبل از این کنترلر، IAiMultimediaStudioService فقط در DI ثبت شده بود ولی هیچ Controller‌ای
    /// آن را صدا نمی‌زد و مصرف اعتبار هرگز به این سرویس متصل نبود — این کنترلر هر دو خلأ را می‌بندد:
    /// ۱) تولید محتوا را واقعاً از طریق HTTP در دسترس مشتری قرار می‌دهد،
    /// ۲) قبل از هر فراخوانی هزینه‌بر AI بیرونی، از طریق IWalletService.TryDebitAsync (کیف‌پول واحد
    ///    پلتفرم) هزینه کسر می‌شود؛ اگر سرویس بیرونی شکست بخورد، مبلغ خودکار بازگردانده می‌شود تا
    ///    مشتری بابت تولید ناموفق هزینه نپردازد.
    /// </summary>
    [ApiController]
    [Route("api/instagram/ai-studio")]
    public class AiMultimediaStudioController : ControllerBase
    {
        // هزینهٔ هر عملیات به تومان (معادل تبدیل‌شدهٔ نرخ قبلی ۱۵٬۰۰۰ تومان به‌ازای هر واحد اعتبار).
        // در نسخهٔ کامل باید این مقادیر به‌صورت تنظیمات قابل‌ویرایش هر تننت (TenantPlan/Setting) باشند.
        private const decimal EnhancePhotoTomanCost = 30000m;
        private const decimal VideoStoryTomanCost = 75000m;
        private const decimal VoiceOverTomanCost = 15000m;
        private const decimal ModelPhotoTomanCost = 45000m;
        private const decimal ModelVideoTomanCost = 95000m;

        // مطابق کامنت داخل AiMultimediaStudioService: دیپ‌فا برای تصویر، آتنا برای ویدیو، ویرا برای صدا.
        private const string ImageProviderKey = "deepfa";
        private const string VideoProviderKey = "atna";
        private const string VoiceProviderKey = "vira";

        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly IWalletService _walletService;
        private readonly ITenantIntegrationCredentialService _credentialService;
        private readonly IAiMultimediaStudioService _studioService;
        private readonly IBackgroundMusicCatalogService _musicCatalogService;
        private readonly MultiTenantStores.Services.ITenantPlanService _tenantPlanService;

        public AiMultimediaStudioController(
            IWorkContext workContext,
            IStoreContext storeContext,
            IWalletService walletService,
            ITenantIntegrationCredentialService credentialService,
            IAiMultimediaStudioService studioService,
            IBackgroundMusicCatalogService musicCatalogService,
            MultiTenantStores.Services.ITenantPlanService tenantPlanService)
        {
            _workContext = workContext;
            _storeContext = storeContext;
            _walletService = walletService;
            _credentialService = credentialService;
            _studioService = studioService;
            _musicCatalogService = musicCatalogService;
            _tenantPlanService = tenantPlanService;
        }

        /// <summary>
        /// استودیوی AI بخشی از «دستیار اینستاگرام» است — طبق ساختار پلن‌های سایت مادر، فقط پلن‌های
        /// نقره‌ای و طلایی به آن دسترسی دارند (نه برنزی/آزمایشی). اگر دسترسی نداشت، پیام ۴۰۲
        /// برمی‌گرداند.
        /// </summary>
        private async Task<IActionResult> CheckInstagramAssistantPlanAsync(int storeId)
        {
            var activePlan = await _tenantPlanService.GetActivePlanForStoreAsync(storeId);
            if (activePlan == null || !activePlan.AllowInstagramAiAssistant)
                return StatusCode(402, new { success = false, message = "استودیوی AI مخصوص پلن‌های نقره‌ای و طلایی است. لطفاً پلن فروشگاه را ارتقا دهید." });

            return null;
        }

        /// <summary>فهرست موسیقی‌های پس‌زمینهٔ قابل‌انتخاب — اپ فلاتر این را قبل از فراخوانی video-story نمایش می‌دهد.</summary>
        [HttpGet("background-music-tracks")]
        public IActionResult GetBackgroundMusicTracks()
        {
            return Ok(new { tracks = _musicCatalogService.GetAvailableTracks() });
        }

        [HttpPost("enhance-photo")]
        public async Task<IActionResult> EnhancePhoto([FromBody] EnhancePhotoRequestDto dto)
        {
            var (customerId, storeId) = await GetCustomerAndStoreIdAsync();

            var planGateResult = await CheckInstagramAssistantPlanAsync(storeId);
            if (planGateResult != null) return planGateResult;

            var apiKey = await ResolveActiveProviderApiKeyAsync(storeId, ImageProviderKey);
            if (apiKey == null)
                return BadRequest(new { success = false, message = "هیچ کلید API فعالی برای سرویس ادیت تصویر (دیپ‌فا) تنظیم نشده است." });

            var referenceCode = $"ai-photo-{Guid.NewGuid():N}";
            var (debited, balanceAfterDebit, debitError) = await _walletService.TryDebitAsync(
                customerId, storeId, EnhancePhotoTomanCost, WalletTransactionReason.AiFeatureUsageDebit, referenceCode);
            if (!debited)
                return StatusCode(402, new { success = false, message = debitError, requiredToman = EnhancePhotoTomanCost, currentBalanceToman = balanceAfterDebit });

            var result = await _studioService.EnhanceProductPhotoAsync(
                apiKey, dto.RawImageUrl, dto.BackgroundPreset, dto.ApplySkuWatermark, dto.SkuCode);

            if (!result.IsSuccess)
            {
                await RefundAsync(customerId, storeId, EnhancePhotoTomanCost, referenceCode);
                return BadRequest(new { success = false, message = result.Message });
            }

            return Ok(new
            {
                success = true,
                enhancedImageUrl = result.EnhancedImageUrl,
                skuWatermarkApplied = result.SkuWatermarkApplied,
                skuCode = result.SkuCode,
                tomanCharged = EnhancePhotoTomanCost,
                newBalanceToman = await _walletService.GetBalanceAsync(customerId, storeId)
            });
        }

        /// <summary>تولید عکس مدل/آواتار به همراه محصول — با یا بدون عکس محصول ورودی (طبق نیازمندی کاربر).</summary>
        [HttpPost("generate-model-photo")]
        public async Task<IActionResult> GenerateModelPhoto([FromBody] GenerateModelPhotoRequestDto dto)
        {
            var (customerId, storeId) = await GetCustomerAndStoreIdAsync();

            var planGateResult = await CheckInstagramAssistantPlanAsync(storeId);
            if (planGateResult != null) return planGateResult;

            var apiKey = await ResolveActiveProviderApiKeyAsync(storeId, ImageProviderKey);
            if (apiKey == null)
                return BadRequest(new { success = false, message = "هیچ کلید API فعالی برای سرویس ادیت تصویر (دیپ‌فا) تنظیم نشده است." });

            var referenceCode = $"ai-model-photo-{Guid.NewGuid():N}";
            var (debited, balanceAfterDebit, debitError) = await _walletService.TryDebitAsync(
                customerId, storeId, ModelPhotoTomanCost, WalletTransactionReason.AiFeatureUsageDebit, referenceCode);
            if (!debited)
                return StatusCode(402, new { success = false, message = debitError, requiredToman = ModelPhotoTomanCost, currentBalanceToman = balanceAfterDebit });

            var result = await _studioService.GenerateModelPhotoAsync(
                apiKey, dto.ModelDescription, dto.ProductImageUrl, dto.ApplySkuWatermark, dto.SkuCode);

            if (!result.IsSuccess)
            {
                await RefundAsync(customerId, storeId, ModelPhotoTomanCost, referenceCode);
                return BadRequest(new { success = false, message = result.Message });
            }

            return Ok(new
            {
                success = true,
                enhancedImageUrl = result.EnhancedImageUrl,
                skuWatermarkApplied = result.SkuWatermarkApplied,
                skuCode = result.SkuCode,
                tomanCharged = ModelPhotoTomanCost,
                newBalanceToman = await _walletService.GetBalanceAsync(customerId, storeId)
            });
        }

        /// <summary>نسخهٔ ویدیویی تولید مدل/آواتار — با یا بدون عکس محصول ورودی.</summary>
        [HttpPost("generate-model-video")]
        public async Task<IActionResult> GenerateModelVideo([FromBody] GenerateModelVideoRequestDto dto)
        {
            var (customerId, storeId) = await GetCustomerAndStoreIdAsync();

            var planGateResult = await CheckInstagramAssistantPlanAsync(storeId);
            if (planGateResult != null) return planGateResult;

            var apiKey = await ResolveActiveProviderApiKeyAsync(storeId, VideoProviderKey);
            if (apiKey == null)
                return BadRequest(new { success = false, message = "هیچ کلید API فعالی برای سرویس تولید ویدیو (آتنا) تنظیم نشده است." });

            var referenceCode = $"ai-model-video-{Guid.NewGuid():N}";
            var (debited, balanceAfterDebit, debitError) = await _walletService.TryDebitAsync(
                customerId, storeId, ModelVideoTomanCost, WalletTransactionReason.AiFeatureUsageDebit, referenceCode);
            if (!debited)
                return StatusCode(402, new { success = false, message = debitError, requiredToman = ModelVideoTomanCost, currentBalanceToman = balanceAfterDebit });

            var result = await _studioService.GenerateModelVideoAsync(
                apiKey, dto.ModelDescription, dto.ProductImageUrl, dto.BackgroundMusicTrackId);

            if (!result.IsSuccess)
            {
                await RefundAsync(customerId, storeId, ModelVideoTomanCost, referenceCode);
                return BadRequest(new { success = false, message = result.Message });
            }

            return Ok(new
            {
                success = true,
                videoStoryUrl = result.VideoStoryUrl,
                durationSeconds = result.DurationSeconds,
                backgroundMusicTrackId = result.BackgroundMusicTrackId,
                tomanCharged = ModelVideoTomanCost,
                newBalanceToman = await _walletService.GetBalanceAsync(customerId, storeId)
            });
        }

        [HttpPost("video-story")]
        public async Task<IActionResult> VideoStory([FromBody] VideoStoryRequestDto dto)
        {
            var (customerId, storeId) = await GetCustomerAndStoreIdAsync();

            var planGateResult = await CheckInstagramAssistantPlanAsync(storeId);
            if (planGateResult != null) return planGateResult;

            var apiKey = await ResolveActiveProviderApiKeyAsync(storeId, VideoProviderKey);
            if (apiKey == null)
                return BadRequest(new { success = false, message = "هیچ کلید API فعالی برای سرویس تولید ویدیو (آتنا) تنظیم نشده است." });

            var referenceCode = $"ai-video-{Guid.NewGuid():N}";
            var (debited, balanceAfterDebit, debitError) = await _walletService.TryDebitAsync(
                customerId, storeId, VideoStoryTomanCost, WalletTransactionReason.AiFeatureUsageDebit, referenceCode);
            if (!debited)
                return StatusCode(402, new { success = false, message = debitError, requiredToman = VideoStoryTomanCost, currentBalanceToman = balanceAfterDebit });

            var result = await _studioService.Generate5SecProductVideoStoryAsync(
                apiKey, dto.ProductId, dto.ProductTitle, dto.PriceToman, dto.BackgroundMusicTrackId);

            if (!result.IsSuccess)
            {
                await RefundAsync(customerId, storeId, VideoStoryTomanCost, referenceCode);
                return BadRequest(new { success = false, message = result.Message });
            }

            return Ok(new
            {
                success = true,
                videoStoryUrl = result.VideoStoryUrl,
                durationSeconds = result.DurationSeconds,
                backgroundMusicTrackId = result.BackgroundMusicTrackId,
                tomanCharged = VideoStoryTomanCost,
                newBalanceToman = await _walletService.GetBalanceAsync(customerId, storeId)
            });
        }

        [HttpPost("voice-over")]
        public async Task<IActionResult> VoiceOver([FromBody] VoiceOverRequestDto dto)
        {
            var (customerId, storeId) = await GetCustomerAndStoreIdAsync();

            var planGateResult = await CheckInstagramAssistantPlanAsync(storeId);
            if (planGateResult != null) return planGateResult;

            var apiKey = await ResolveActiveProviderApiKeyAsync(storeId, VoiceProviderKey);
            if (apiKey == null)
                return BadRequest(new { success = false, message = "هیچ کلید API فعالی برای سرویس تبدیل متن به گفتار (ویرا) تنظیم نشده است." });

            var referenceCode = $"ai-voice-{Guid.NewGuid():N}";
            var (debited, balanceAfterDebit, debitError) = await _walletService.TryDebitAsync(
                customerId, storeId, VoiceOverTomanCost, WalletTransactionReason.AiFeatureUsageDebit, referenceCode);
            if (!debited)
                return StatusCode(402, new { success = false, message = debitError, requiredToman = VoiceOverTomanCost, currentBalanceToman = balanceAfterDebit });

            var result = await _studioService.GeneratePersianVoiceOverAsync(
                apiKey, dto.TextToSpeak, dto.SpeakerVoiceGender ?? "Female");

            if (!result.IsSuccess)
            {
                await RefundAsync(customerId, storeId, VoiceOverTomanCost, referenceCode);
                return BadRequest(new { success = false, message = result.Message });
            }

            return Ok(new
            {
                success = true,
                audioMp3Url = result.AudioMp3Url,
                speaker = result.Speaker,
                tomanCharged = VoiceOverTomanCost,
                newBalanceToman = await _walletService.GetBalanceAsync(customerId, storeId)
            });
        }

        private async Task<(int customerId, int storeId)> GetCustomerAndStoreIdAsync()
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            var store = await _storeContext.GetCurrentStoreAsync();
            return (customer.Id, store.Id);
        }

        private async Task<string> ResolveActiveProviderApiKeyAsync(int storeId, string providerKey)
        {
            var credentials = await _credentialService.GetByStoreIdAsync(storeId);
            var credential = credentials.FirstOrDefault(c => c.ProviderKey == providerKey && c.IsActive);
            return credential == null ? null : _credentialService.DecryptForActualUse(credential.ApiKey);
        }

        /// <summary>
        /// بازگشت خودکار مبلغ پس از شکست واقعی سرویس AI بیرونی. Reference جدا از کد اصلی تا در
        /// کیف‌پول، اصل کسر و بازگشتش قابل ردیابی مجزا باشند (نه بازنویسی همان ردیف).
        /// </summary>
        private Task RefundAsync(int customerId, int storeId, decimal amountToman, string originalReferenceCode)
        {
            return _walletService.CreditAsync(
                customerId, storeId, amountToman, WalletTransactionReason.AiFeatureUsageRefund, $"{originalReferenceCode}-refund");
        }
    }

    public class EnhancePhotoRequestDto
    {
        public string RawImageUrl { get; set; }
        public string BackgroundPreset { get; set; }
        public bool ApplySkuWatermark { get; set; }
        public string SkuCode { get; set; }
    }

    public class VideoStoryRequestDto
    {
        public string ProductId { get; set; }
        public string ProductTitle { get; set; }
        public decimal PriceToman { get; set; }
        public string BackgroundMusicTrackId { get; set; }
    }

    public class VoiceOverRequestDto
    {
        public string TextToSpeak { get; set; }
        public string SpeakerVoiceGender { get; set; }
    }

    public class GenerateModelPhotoRequestDto
    {
        public string ModelDescription { get; set; }

        /// <summary>اختیاری — اگر خالی باشد، تصویر کاملاً از روی توضیح متنی ساخته می‌شود.</summary>
        public string ProductImageUrl { get; set; }
        public bool ApplySkuWatermark { get; set; }
        public string SkuCode { get; set; }
    }

    public class GenerateModelVideoRequestDto
    {
        public string ModelDescription { get; set; }
        public string ProductImageUrl { get; set; }
        public string BackgroundMusicTrackId { get; set; }
    }
}
