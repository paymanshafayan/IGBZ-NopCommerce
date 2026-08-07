namespace Nop.Plugin.Misc.MultiTenantStores.Services
{
    using System;
    using System.Linq;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Nop.Data;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;
    using Nop.Services.Security;

    public interface ITenantIntegrationCredentialService
    {
        Task<System.Collections.Generic.IList<TenantIntegrationCredential>> GetByStoreIdAsync(int storeId);

        /// <summary>
        /// همهٔ اعتبارنامه‌های فعال یک Provider، مستقل از فروشگاه — لازم برای Webhookهای بیرونی
        /// (مثل mentions اینستاگرام) که یک نقطهٔ ورود مشترک دارند و باید بر اساس محتوای Payload
        /// (نه Subdomain درخواست) به تننت درست نگاشت شوند.
        /// </summary>
        Task<System.Collections.Generic.IList<TenantIntegrationCredential>> GetAllActiveByProviderKeyAsync(string providerKey);
        Task<TenantIntegrationCredential> GetByIdAsync(int id);
        Task<TenantIntegrationCredential> SaveAsync(int? id, int storeId, string providerKey, string apiKeyPlainOrNull,
            string apiSecretPlainOrNull, string endpointOverrideUrl, bool isActive);
        Task DeleteAsync(int id);
        string DecryptForDisplayMasked(string encryptedValue);
        string DecryptForActualUse(string encryptedValue);
        Task<CredentialTestResult> TestConnectionAsync(int id);

        /// <summary>لیست شناسه‌های Providerهایی که این پلتفرم پشتیبانی می‌کند (بخش ۱۰.۴ سند معماری)</summary>
        System.Collections.Generic.IReadOnlyList<string> GetKnownProviderKeys();

        /// <summary>متادیتای نمایشی هر Provider (نام فارسی + لینک راهنمای دریافت کلید API)</summary>
        ProviderMetadata GetProviderMetadata(string providerKey);
    }

    public class ProviderMetadata
    {
        public string ProviderKey { get; set; }
        public string DisplayName { get; set; }
        public string GuideUrl { get; set; }
    }

    public class TenantIntegrationCredentialService : ITenantIntegrationCredentialService
    {
        // متادیتای هر Provider — نام فارسی و لینک مستقیم راهنمای دریافت API Key (نیازمندی #۳ فهرست ویژگی‌ها).
        // این آدرس‌ها باید دوره‌ای بازبینی شوند چون سرویس‌های بیرونی مستندات خود را جابه‌جا می‌کنند.
        private static readonly System.Collections.Generic.Dictionary<string, ProviderMetadata> Providers = new()
        {
            ["parbad.zarinpal"] = new() { DisplayName = "زرین‌پال (Parbad)", GuideUrl = "https://help.zarinpal.com/" },
            ["parbad.mellat"] = new() { DisplayName = "به‌پرداخت ملت (Parbad)", GuideUrl = "https://behpardakht.com/" },
            ["parbad.saman"] = new() { DisplayName = "سامان (سپ) (Parbad)", GuideUrl = "https://sep.shaparak.ir/" },
            ["parbad.parsian"] = new() { DisplayName = "پارسیان (Parbad)", GuideUrl = "https://pec.shaparak.ir/" },
            ["parbad.pasargad"] = new() { DisplayName = "پاسارگاد (Parbad)", GuideUrl = "https://pep.shaparak.ir/" },
            ["snapppay"] = new() { DisplayName = "اسنپ‌پی (BNPL)", GuideUrl = "https://snapppay.ir/merchant-api-guide" },
            ["nowpayments"] = new() { DisplayName = "NOWPayments (رمزارز)", GuideUrl = "https://nowpayments.io/api-docs" },
            ["digikala"] = new() { DisplayName = "دیجی‌کالا (Seller Open API)", GuideUrl = "https://seller.digikala.com/open-api/v1/doc/" },
            ["divar"] = new() { DisplayName = "کنار دیوار", GuideUrl = "https://divar.ir/kenar" },
            ["torob"] = new() { DisplayName = "ترب (Torob)", GuideUrl = "https://torob.com/" },
            ["tapin"] = new() { DisplayName = "تاپین (لجستیک)", GuideUrl = "https://tapin.ir/developers" },
            ["postex"] = new() { DisplayName = "پستکس (لجستیک)", GuideUrl = "https://staging.api.postex.ir" },
            ["sepidar"] = new() { DisplayName = "سپیدار سیستم (حسابداری)", GuideUrl = "https://www.sepidarsystem.com/" },
            ["rahkaran"] = new() { DisplayName = "راهکاران ابری (حسابداری)", GuideUrl = "https://rahkaran.rayvarz.com/" },
            ["modaian"] = new() { DisplayName = "سامانه مؤدیان مالیاتی", GuideUrl = "https://tp.tax.gov.ir/" },
            ["deepfa"] = new() { DisplayName = "دیپ‌فا (AI چندرسانه‌ای)", GuideUrl = "https://deepfa.ir/tools" },
            ["atna"] = new() { DisplayName = "آتنا AI", GuideUrl = "https://athenai.app" },
            ["digimark"] = new() { DisplayName = "دیجی‌مارک (AI تصویر)", GuideUrl = "https://digimark-ai.com" },
            ["vira"] = new() { DisplayName = "ویرا (iVira - صدا)", GuideUrl = "https://ivira.ai" },
            ["tarjomyar"] = new() { DisplayName = "ترجمیار (ترجمه)", GuideUrl = "https://tarjomyar.ir/api" },
            ["farazin"] = new() { DisplayName = "فرازین (ترجمه)", GuideUrl = "https://farazin.io/" },
            ["mizbanbot"] = new() { DisplayName = "میزبان‌بات (سئو)", GuideUrl = "https://mizbanbot.com/" },
            ["yektanet"] = new() { DisplayName = "یکتانت (تبلیغات)", GuideUrl = "https://yektanet.com/" },
            ["tapsell"] = new() { DisplayName = "تپسل (تبلیغات)", GuideUrl = "https://tapsell.ir/" },
            ["kavenegar"] = new() { DisplayName = "کاوه‌نگار (پیامک/OTP)", GuideUrl = "https://kavenegar.com/rest.html" },
            ["triboon"] = new() { DisplayName = "تریبون (رپورتاژ)", GuideUrl = "https://triboon.co/" },
            ["instagram.graph"] = new() { DisplayName = "Instagram Graph API (توکن دسترسی صفحه/کسب‌وکار)", GuideUrl = "https://developers.facebook.com/docs/instagram-api/" },
            ["instagram.oauth"] = new() { DisplayName = "ورود مشتریان با اینستاگرام (Business Login App ID/Secret)", GuideUrl = "https://developers.facebook.com/docs/instagram-platform/instagram-api-with-instagram-login/business-login" }
        };

        private readonly IRepository<TenantIntegrationCredential> _credentialRepository;
        private readonly IEncryptionService _encryptionService;
        private readonly IHttpClientFactory _httpClientFactory;

        public TenantIntegrationCredentialService(
            IRepository<TenantIntegrationCredential> credentialRepository,
            IEncryptionService encryptionService,
            IHttpClientFactory httpClientFactory)
        {
            _credentialRepository = credentialRepository;
            _encryptionService = encryptionService;
            _httpClientFactory = httpClientFactory;
        }

        public System.Collections.Generic.IReadOnlyList<string> GetKnownProviderKeys() => Providers.Keys.ToList();

        public ProviderMetadata GetProviderMetadata(string providerKey)
        {
            if (providerKey != null && Providers.TryGetValue(providerKey, out var meta))
            {
                meta.ProviderKey = providerKey;
                return meta;
            }
            return new ProviderMetadata { ProviderKey = providerKey, DisplayName = providerKey, GuideUrl = null };
        }

        public async Task<System.Collections.Generic.IList<TenantIntegrationCredential>> GetByStoreIdAsync(int storeId)
        {
            return await _credentialRepository.GetAllAsync(q =>
                q.Where(c => c.StoreId == storeId).OrderBy(c => c.ProviderKey));
        }

        public async Task<System.Collections.Generic.IList<TenantIntegrationCredential>> GetAllActiveByProviderKeyAsync(string providerKey)
        {
            return await _credentialRepository.GetAllAsync(q =>
                q.Where(c => c.ProviderKey == providerKey && c.IsActive));
        }

        public async Task<TenantIntegrationCredential> GetByIdAsync(int id)
        {
            return await _credentialRepository.GetByIdAsync(id);
        }

        /// <summary>
        /// ذخیرهٔ کلید/راز. اگر مقدار جدید کلید/راز خالی باشد (کاربر فیلد را خالی گذاشته)، مقدار
        /// رمزنگاری‌شدهٔ قبلی حفظ می‌شود — یعنی فرم هرگز کلید موجود را با یک مقدار خالی/جعلی جایگزین نمی‌کند.
        /// </summary>
        public async Task<TenantIntegrationCredential> SaveAsync(int? id, int storeId, string providerKey,
            string apiKeyPlainOrNull, string apiSecretPlainOrNull, string endpointOverrideUrl, bool isActive)
        {
            if (string.IsNullOrWhiteSpace(providerKey))
                throw new ArgumentException("شناسهٔ Provider الزامی است.", nameof(providerKey));

            TenantIntegrationCredential entity;
            var now = DateTime.UtcNow;

            if (id.HasValue && id.Value > 0)
            {
                entity = await _credentialRepository.GetByIdAsync(id.Value)
                    ?? throw new InvalidOperationException($"رکورد اعتبارنامه با شناسه {id.Value} یافت نشد.");

                if (entity.StoreId != storeId)
                    throw new UnauthorizedAccessException("این رکورد متعلق به فروشگاه دیگری است.");

                if (!string.IsNullOrWhiteSpace(apiKeyPlainOrNull))
                {
                    entity.ApiKey = _encryptionService.EncryptText(apiKeyPlainOrNull);
                    // تغییر واقعیِ کلید = اعتبار قبلی دیگر معتبر فرض نمی‌شود؛ باید دوباره تست شود
                    entity.IsVerified = false;
                    entity.LastTestedOnUtc = null;
                    entity.LastTestResultMessage = null;
                }

                if (!string.IsNullOrWhiteSpace(apiSecretPlainOrNull))
                {
                    entity.ApiSecret = _encryptionService.EncryptText(apiSecretPlainOrNull);
                    entity.IsVerified = false;
                    entity.LastTestedOnUtc = null;
                    entity.LastTestResultMessage = null;
                }

                entity.EndpointOverrideUrl = endpointOverrideUrl;
                entity.IsActive = isActive;
                entity.UpdatedOnUtc = now;

                await _credentialRepository.UpdateAsync(entity);
            }
            else
            {
                entity = new TenantIntegrationCredential
                {
                    StoreId = storeId,
                    ProviderKey = providerKey.Trim().ToLowerInvariant(),
                    ApiKey = string.IsNullOrWhiteSpace(apiKeyPlainOrNull) ? null : _encryptionService.EncryptText(apiKeyPlainOrNull),
                    ApiSecret = string.IsNullOrWhiteSpace(apiSecretPlainOrNull) ? null : _encryptionService.EncryptText(apiSecretPlainOrNull),
                    EndpointOverrideUrl = endpointOverrideUrl,
                    IsActive = isActive,
                    IsVerified = false,
                    CreatedOnUtc = now,
                    UpdatedOnUtc = now
                };

                await _credentialRepository.InsertAsync(entity);
            }

            return entity;
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _credentialRepository.GetByIdAsync(id);
            if (entity != null)
                await _credentialRepository.DeleteAsync(entity);
        }

        /// <summary>
        /// نمایش امن: فقط ۴ کاراکتر آخر کلید رمزگشایی‌شده نشان داده می‌شود، هیچ‌گاه مقدار کامل.
        /// </summary>
        /// <summary>
        /// رمزگشایی واقعی برای مصرف در فراخوانی API بیرونی (نه نمایش). هرگز نباید خروجی این متد
        /// در هیچ View/Log/Response‌ای نمایش داده شود — فقط برای ساخت درخواست HTTP واقعی است.
        /// </summary>
        public string DecryptForActualUse(string encryptedValue)
        {
            if (string.IsNullOrEmpty(encryptedValue))
                return string.Empty;

            return _encryptionService.DecryptText(encryptedValue);
        }

        public string DecryptForDisplayMasked(string encryptedValue)
        {
            if (string.IsNullOrEmpty(encryptedValue))
                return string.Empty;

            string plain;
            try
            {
                plain = _encryptionService.DecryptText(encryptedValue);
            }
            catch
            {
                return "•••• (قابل رمزگشایی نیست)";
            }

            if (string.IsNullOrEmpty(plain))
                return string.Empty;

            return plain.Length <= 4
                ? new string('•', plain.Length)
                : new string('•', plain.Length - 4) + plain[^4..];
        }

        /// <summary>
        /// تست اتصال واقعی: فقط بررسی می‌کند سرور مقصد قابل‌دسترس است (پاسخ HTTP دریافت می‌شود، حتی
        /// ۴xx/۵xx). این معادل «اعتبارسنجی صحت کلید API» نیست — چون هر Provider قرارداد احراز هویت
        /// متفاوتی دارد (بخش ۱۰.۲ سند معماری برای تست اختصاصی هر Provider). به همین دلیل نتیجه در
        /// پیام صریحاً همین محدودیت را اعلام می‌کند، تا این دکمه هرگز به یک «تیک جعلی» تبدیل نشود.
        /// </summary>
        public async Task<CredentialTestResult> TestConnectionAsync(int id)
        {
            var entity = await _credentialRepository.GetByIdAsync(id)
                ?? throw new InvalidOperationException($"رکورد اعتبارنامه با شناسه {id} یافت نشد.");

            if (string.IsNullOrWhiteSpace(entity.EndpointOverrideUrl))
            {
                entity.LastTestedOnUtc = DateTime.UtcNow;
                entity.LastTestResultMessage = "آدرس Endpoint تنظیم نشده — تست اتصال شبکه‌ای ممکن نیست. " +
                    "این فقط یعنی سرور در دسترس است، نه این‌که کلید/راز صحیح است؛ اعتبارسنجی واقعی کلید در اولین تراکنش واقعی انجام می‌شود.";
                entity.IsVerified = false;
                await _credentialRepository.UpdateAsync(entity);

                return new CredentialTestResult { Success = false, Message = entity.LastTestResultMessage };
            }

            var httpClient = _httpClientFactory.CreateClient("IntegrationCredentialTest");
            httpClient.Timeout = TimeSpan.FromSeconds(8);

            string resultMessage;
            bool reachable;
            try
            {
                var response = await httpClient.GetAsync(entity.EndpointOverrideUrl);
                reachable = true;
                resultMessage = $"سرور در آدرس تنظیم‌شده پاسخ داد (کد HTTP {(int)response.StatusCode}). " +
                    "توجه: این فقط یعنی سرور در دسترس است؛ صحت کلید/راز API فقط با یک تراکنش واقعی محرز می‌شود.";
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                reachable = false;
                resultMessage = $"اتصال به آدرس تنظیم‌شده برقرار نشد: {ex.Message}";
            }

            entity.LastTestedOnUtc = DateTime.UtcNow;
            entity.LastTestResultMessage = resultMessage;
            entity.IsVerified = reachable; // فقط «قابل‌دسترس‌بودن»، نه صحت کامل اعتبارنامه
            await _credentialRepository.UpdateAsync(entity);

            return new CredentialTestResult { Success = reachable, Message = resultMessage };
        }
    }

    public class CredentialTestResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
