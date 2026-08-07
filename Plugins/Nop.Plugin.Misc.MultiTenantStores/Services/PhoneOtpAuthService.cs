namespace Nop.Plugin.Misc.MultiTenantStores.Services
{
    using System;
    using System.Linq;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Caching.Memory;
    using Nop.Core.Domain.Customers;
    using Nop.Services.Common;
    using Nop.Services.Customers;

    public class PhoneOtpVerifyResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public int CustomerId { get; set; }
        public bool IsNewCustomer { get; set; }
        public string AccessToken { get; set; }
    }

    /// <summary>
    /// ورود با شماره موبایل + کد یک‌بارمصرف پیامکی — مسیر جایگزین *واقعی* (نه فقط توصیه‌شده در کامنت)
    /// برای مشتریانی که حساب اینستاگرام شخصی دارند و طبق محدودیت مستندشده در InstagramLoginController
    /// اصلاً نمی‌توانند از Business Login for Instagram استفاده کنند. از سرویس پیامک کاوه‌نگار
    /// (ProviderKey = kavenegar) استفاده می‌کند که پیش از این فقط در فهرست Providerها تعریف شده بود
    /// ولی هیچ‌جای کدبیس واقعاً فراخوانی نمی‌شد.
    /// </summary>
    public interface IPhoneOtpAuthService
    {
        Task<(bool success, string errorMessage)> RequestOtpAsync(int storeId, string phoneNumber);
        Task<PhoneOtpVerifyResult> VerifyOtpAsync(int storeId, string phoneNumber, string code);
    }

    public class PhoneOtpAuthService : IPhoneOtpAuthService
    {
        private const string ProviderKey = "kavenegar";
        private const int OtpLength = 5;
        private static readonly TimeSpan OtpTtl = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ResendThrottle = TimeSpan.FromSeconds(60);

        private readonly ITenantIntegrationCredentialService _credentialService;
        private readonly ICustomerService _customerService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IJwtTokenService _jwtTokenService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _memoryCache;

        public PhoneOtpAuthService(
            ITenantIntegrationCredentialService credentialService,
            ICustomerService customerService,
            IGenericAttributeService genericAttributeService,
            IJwtTokenService jwtTokenService,
            IHttpClientFactory httpClientFactory,
            IMemoryCache memoryCache)
        {
            _credentialService = credentialService;
            _customerService = customerService;
            _genericAttributeService = genericAttributeService;
            _jwtTokenService = jwtTokenService;
            _httpClientFactory = httpClientFactory;
            _memoryCache = memoryCache;
        }

        public async Task<(bool success, string errorMessage)> RequestOtpAsync(int storeId, string phoneNumber)
        {
            var normalizedPhone = NormalizePhone(phoneNumber);
            if (normalizedPhone == null)
                return (false, "شمارهٔ موبایل نامعتبر است (فرمت مورد انتظار: ۰۹xxxxxxxxx).");

            var throttleKey = $"otp-throttle:{storeId}:{normalizedPhone}";
            if (_memoryCache.TryGetValue(throttleKey, out _))
                return (false, "لطفاً قبل از درخواست مجدد کد، حداقل ۶۰ ثانیه صبر کنید.");

            var credentials = await _credentialService.GetByStoreIdAsync(storeId);
            var credential = credentials.FirstOrDefault(c => c.ProviderKey == ProviderKey && c.IsActive);
            if (credential == null)
                return (false, "سرویس پیامک (کاوه‌نگار) برای این فروشگاه تنظیم نشده است.");

            var apiKey = _credentialService.DecryptForActualUse(credential.ApiKey);
            var code = GenerateNumericCode(OtpLength);

            var httpClient = _httpClientFactory.CreateClient("KavenegarSms");

            // ⚠️ از Send API عمومی کاوه‌نگار استفاده شده. اگر تننت یک قالب OTP تاییدشده (Verify
            // Lookup API) دارد، برای عبور بهتر از فیلترهای اپراتور بهتر است از
            // https://api.kavenegar.com/v1/{API-KEY}/verify/lookup.json به‌همراه نام قالب استفاده شود.
            var message = Uri.EscapeDataString($"کد ورود شما: {code}");
            var sendUrl = $"https://api.kavenegar.com/v1/{apiKey}/sms/send.json" +
                $"?receptor={Uri.EscapeDataString(normalizedPhone)}&message={message}";

            try
            {
                var response = await httpClient.PostAsync(sendUrl, null);
                if (!response.IsSuccessStatusCode)
                    return (false, "ارسال پیامک ناموفق بود.");
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                return (false, $"اتصال به سرویس پیامک برقرار نشد: {ex.Message}");
            }

            _memoryCache.Set(BuildOtpCacheKey(storeId, normalizedPhone), code, OtpTtl);
            _memoryCache.Set(throttleKey, true, ResendThrottle);

            return (true, null);
        }

        public async Task<PhoneOtpVerifyResult> VerifyOtpAsync(int storeId, string phoneNumber, string code)
        {
            var normalizedPhone = NormalizePhone(phoneNumber);
            if (normalizedPhone == null)
                return new PhoneOtpVerifyResult { Success = false, ErrorMessage = "شمارهٔ موبایل نامعتبر است." };

            var otpKey = BuildOtpCacheKey(storeId, normalizedPhone);
            if (!_memoryCache.TryGetValue(otpKey, out string cachedCode) || cachedCode != code?.Trim())
                return new PhoneOtpVerifyResult { Success = false, ErrorMessage = "کد وارد شده نامعتبر یا منقضی‌شده است." };

            _memoryCache.Remove(otpKey);

            // ICustomerService متد تک‌نتیجه‌ای GetCustomerByPhone ندارد؛ همان الگوی تاییدشدهٔ
            // DeepLinkRoutingController.ResolveStoreByPhone استفاده می‌شود.
            var matchingCustomers = await _customerService.GetAllCustomersAsync(phone: normalizedPhone, pageSize: 1);
            var customer = matchingCustomers.FirstOrDefault();
            var isNewCustomer = customer == null;

            if (customer == null)
            {
                customer = new Customer
                {
                    CustomerGuid = Guid.NewGuid(),
                    Email = $"phone-{normalizedPhone}@customer.igbz.local",
                    RegisteredInStoreId = storeId,
                    Active = true,
                    CreatedOnUtc = DateTime.UtcNow,
                    LastActivityDateUtc = DateTime.UtcNow
                };
                await _customerService.InsertCustomerAsync(customer);

                // ⚠️ عمداً روی customer.Phone مستقیم Set نمی‌شود: GetAllCustomersAsync(phone: ...)
                // در nopCommerce برای جست‌وجو از GenericAttribute استاندارد (NopCustomerDefaults.
                // PhoneAttribute) استفاده می‌کند، نه یک ستون مستقیم روی Customer. اگر این‌جا فقط
                // خاصیت مستقیم Set می‌شد، دفعهٔ بعد که همین کاربر با همان شماره وارد می‌شد، جست‌وجو
                // او را پیدا نمی‌کرد و یک مشتری تکراری دوباره ساخته می‌شد.
                await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.PhoneAttribute, normalizedPhone);

                var registeredRole = await _customerService.GetCustomerRoleBySystemNameAsync(NopCustomerDefaults.RegisteredRoleName);
                if (registeredRole != null)
                {
                    await _customerService.AddCustomerRoleMappingAsync(new CustomerCustomerRoleMapping
                    {
                        CustomerId = customer.Id,
                        CustomerRoleId = registeredRole.Id
                    });
                }
            }

            var accessToken = _jwtTokenService.GenerateAccessToken(customer.Id, storeId);

            return new PhoneOtpVerifyResult
            {
                Success = true,
                CustomerId = customer.Id,
                IsNewCustomer = isNewCustomer,
                AccessToken = accessToken
            };
        }

        private static string BuildOtpCacheKey(int storeId, string normalizedPhone) => $"otp-code:{storeId}:{normalizedPhone}";

        private static string GenerateNumericCode(int length)
        {
            var max = (int)Math.Pow(10, length);
            var value = RandomNumberGenerator.GetInt32(0, max);
            return value.ToString(new string('0', length));
        }

        /// <summary>نرمال‌سازی سادهٔ شمارهٔ موبایل ایران؛ فقط ارقام را نگه می‌دارد و به فرمت ۰۹xxxxxxxxx برمی‌گرداند.</summary>
        private static string NormalizePhone(string phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber)) return null;
            var digitsOnly = new string(phoneNumber.Where(char.IsDigit).ToArray());

            if (digitsOnly.StartsWith("0098")) digitsOnly = "0" + digitsOnly[4..];
            else if (digitsOnly.StartsWith("98")) digitsOnly = "0" + digitsOnly[2..];

            return digitsOnly.Length == 11 && digitsOnly.StartsWith("09") ? digitsOnly : null;
        }
    }
}
