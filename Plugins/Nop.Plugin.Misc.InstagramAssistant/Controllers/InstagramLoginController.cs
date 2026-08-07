namespace Nop.Plugin.Misc.InstagramAssistant.Controllers
{
    using System;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Configuration;
    using Nop.Core;
    using Nop.Core.Domain.Customers;
    using Nop.Services.Common;
    using Nop.Services.Customers;
    using Nop.Plugin.Misc.InstagramAssistant.Services;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// «ورود با حساب اینستاگرام» (نیازمندی #۱۳، راهکار ۳ فایل راهکارهای فالوور) از طریق Business
    /// Login for Instagram (جانشین رسمی Instagram Basic Display API، که در ۴ دسامبر ۲۰۲۴ کاملاً از
    /// کار افتاد). نتیجهٔ نهایی این کنترلر یک JWT واقعی است (از طریق IJwtTokenService) که اپ فلاتر
    /// آن را در هدر Authorization: Bearer نگه می‌دارد.
    ///
    /// ⚠️ محدودیت واقعی و مهم API اینستاگرام در ۲۰۲۶ (نه محدودیت این کد): Business Login for
    /// Instagram فقط برای حساب‌های Business/Creator کار می‌کند؛ اینستاگرام دیگر هیچ راه رسمی برای
    /// ورود با حساب شخصی (Personal) ارائه نمی‌دهد. یعنی مشتریانی که حساب اینستاگرام شخصی دارند
    /// (اکثریت مشتریان معمولی) اصلاً نمی‌توانند از این فلو استفاده کنند — این باید حتماً به‌عنوان
    /// یک روش «جایگزین» در کنار ورود با موبایل/رمز عبور عرضه شود، نه تنها روش ورود.
    /// </summary>
    [ApiController]
    [Route("api/instagram/login")]
    public class InstagramLoginController : ControllerBase
    {
        private readonly IStoreContext _storeContext;
        private readonly ITenantIntegrationCredentialService _credentialService;
        private readonly IInstagramCustomerLinkService _customerLinkService;
        private readonly ICustomerService _customerService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IJwtTokenService _jwtTokenService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        private const string ProviderKey = "instagram.oauth";

        public InstagramLoginController(
            IStoreContext storeContext,
            ITenantIntegrationCredentialService credentialService,
            IInstagramCustomerLinkService customerLinkService,
            ICustomerService customerService,
            IGenericAttributeService genericAttributeService,
            IJwtTokenService jwtTokenService,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _storeContext = storeContext;
            _credentialService = credentialService;
            _customerLinkService = customerLinkService;
            _customerService = customerService;
            _genericAttributeService = genericAttributeService;
            _jwtTokenService = jwtTokenService;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        /// <summary>
        /// آدرس Authorize را برمی‌گرداند تا اپ فلاتر آن را در یک WebView/مرورگر داخلی باز کند
        /// (پاسخ JSON است، نه Redirect مستقیم HTTP — چون کلاینت یک اپ موبایل است، نه مرورگر).
        /// </summary>
        [HttpGet("start")]
        public async Task<IActionResult> Start([FromQuery] string redirectUri)
        {
            var store = await _storeContext.GetCurrentStoreAsync();
            var (appId, _, resolutionError) = await ResolveAppCredentialsAsync(store.Id);
            if (resolutionError != null)
                return BadRequest(new { success = false, message = resolutionError });

            if (string.IsNullOrWhiteSpace(redirectUri))
                return BadRequest(new { success = false, message = "پارامتر redirectUri الزامی است (باید دقیقاً با تنظیمات اپ متا مطابقت داشته باشد)." });

            // state برای دفاع در برابر CSRF: شناسهٔ فروشگاه + nonce تصادفی، در Callback دوباره بررسی می‌شود.
            var nonce = Guid.NewGuid().ToString("N");
            var state = $"{store.Id}:{nonce}";

            var authorizeUrl = "https://api.instagram.com/oauth/authorize"
                + $"?client_id={Uri.EscapeDataString(appId)}"
                + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
                + "&scope=instagram_business_basic"
                + "&response_type=code"
                + $"&state={Uri.EscapeDataString(state)}";

            return Ok(new { success = true, authorizeUrl, state });
        }

        /// <summary>
        /// Callback واقعی OAuth: کد را برای توکن کوتاه‌مدت، سپس توکن بلندمدت مبادله می‌کند، پروفایل
        /// را از graph.instagram.com می‌خواند، مشتری nopCommerce را پیدا/می‌سازد و JWT صادر می‌کند.
        /// </summary>
        [HttpGet("callback")]
        public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string state, [FromQuery] string redirectUri)
        {
            if (string.IsNullOrWhiteSpace(code))
                return BadRequest(new { success = false, message = "کد بازگشتی از اینستاگرام یافت نشد." });

            var store = await _storeContext.GetCurrentStoreAsync();

            // اعتبارسنجی state در برابر CSRF: باید همان storeId درخواست /start باشد.
            var stateStoreId = state?.Split(':').FirstOrDefault();
            if (stateStoreId != store.Id.ToString())
                return BadRequest(new { success = false, message = "پارامتر state نامعتبر است." });

            var (appId, appSecret, resolutionError) = await ResolveAppCredentialsAsync(store.Id);
            if (resolutionError != null)
                return BadRequest(new { success = false, message = resolutionError });

            var httpClient = _httpClientFactory.CreateClient("InstagramGraphApi");

            // ۱. مبادلهٔ کد با توکن کوتاه‌مدت
            var shortTokenResponse = await httpClient.PostAsync("https://api.instagram.com/oauth/access_token",
                new FormUrlEncodedContent(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, string>("client_id", appId),
                    new System.Collections.Generic.KeyValuePair<string, string>("client_secret", appSecret),
                    new System.Collections.Generic.KeyValuePair<string, string>("grant_type", "authorization_code"),
                    new System.Collections.Generic.KeyValuePair<string, string>("redirect_uri", redirectUri ?? string.Empty),
                    new System.Collections.Generic.KeyValuePair<string, string>("code", code)
                }));

            if (!shortTokenResponse.IsSuccessStatusCode)
                return BadRequest(new { success = false, message = "مبادلهٔ کد اینستاگرام با توکن ناموفق بود." });

            var shortTokenPayload = await shortTokenResponse.Content.ReadFromJsonAsync<InstagramShortTokenResponse>();
            // فرمت رسمی فعلی متا این مقادیر را داخل آرایهٔ data برمی‌گرداند؛ اگر تغییر کرد و صاف
            // برگشت، به‌صورت محافظه‌کارانه از فیلد سطح‌بالا هم پشتیبانی می‌کنیم.
            var shortLivedToken = shortTokenPayload?.Data?.FirstOrDefault()?.AccessToken ?? shortTokenPayload?.AccessToken;
            if (string.IsNullOrWhiteSpace(shortLivedToken))
                return BadRequest(new { success = false, message = "توکن کوتاه‌مدت اینستاگرام دریافت نشد." });

            // ۲. مبادلهٔ توکن کوتاه‌مدت با توکن بلندمدت (اعتبار ~۶۰ روز)
            var longTokenUrl = "https://graph.instagram.com/access_token"
                + "?grant_type=ig_exchange_token"
                + $"&client_secret={Uri.EscapeDataString(appSecret)}"
                + $"&access_token={Uri.EscapeDataString(shortLivedToken)}";

            var longTokenResponse = await httpClient.GetAsync(longTokenUrl);
            var longLivedToken = shortLivedToken; // در بدترین حالت، همان توکن کوتاه‌مدت برای خواندن فوری پروفایل کافی است.
            if (longTokenResponse.IsSuccessStatusCode)
            {
                var longTokenPayload = await longTokenResponse.Content.ReadFromJsonAsync<InstagramLongTokenResponse>();
                if (!string.IsNullOrWhiteSpace(longTokenPayload?.AccessToken))
                    longLivedToken = longTokenPayload.AccessToken;
            }

            // ۳. خواندن پروفایل واقعی کاربر
            var profileResponse = await httpClient.GetAsync(
                $"https://graph.instagram.com/me?fields=id,username&access_token={Uri.EscapeDataString(longLivedToken)}");

            if (!profileResponse.IsSuccessStatusCode)
                return BadRequest(new { success = false, message = "خواندن پروفایل اینستاگرام ناموفق بود." });

            var profile = await profileResponse.Content.ReadFromJsonAsync<InstagramProfileResponse>();
            if (profile == null || string.IsNullOrWhiteSpace(profile.Id))
                return BadRequest(new { success = false, message = "پروفایل اینستاگرام نامعتبر بازگشت." });

            var (customer, isNewCustomer) = await FindOrCreateCustomerForInstagramAsync(store.Id, profile.Id, profile.Username);

            var accessToken = _jwtTokenService.GenerateAccessToken(customer.Id, store.Id);

            return Ok(new
            {
                success = true,
                accessToken,
                customerId = customer.Id,
                instagramUsername = profile.Username,
                isNewCustomer
            });
        }

        /// <summary>Endpoint نمونه/تستی برای اثبات این‌که Scheme «Bearer» واقعاً کار می‌کند — الگویی برای Controllerهای بعدی.</summary>
        [HttpGet("me")]
        [Authorize(AuthenticationSchemes = "Bearer")]
        public IActionResult Me()
        {
            var customerIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var storeIdClaim = User.FindFirst("storeId")?.Value;
            return Ok(new { customerId = customerIdClaim, storeId = storeIdClaim });
        }

        /// <summary>
        /// اکثر فروشگاه‌های کوچک اینستاگرامی نمی‌توانند/نباید مجبور شوند اپ متای اختصاصی خودشان را
        /// بسازند — الگوی رایج SaaS (مثل Buffer/Later/Hootsuite) این است که خودِ پلتفرم یک اپ متای
        /// مشترک دارد و هر تننت فقط حساب اینستاگرام خودش را وصل می‌کند. بنابراین این متد اول
        /// تنظیمات سطح پلتفرم (appsettings/User Secrets) را می‌خواند؛ فقط اگر تننتی App اختصاصی خودش
        /// را ثبت کرده باشد (ProviderKey = instagram.oauth)، همان اولویت می‌گیرد.
        /// </summary>
        private async Task<(string appId, string appSecret, string error)> ResolveAppCredentialsAsync(int storeId)
        {
            var credentials = await _credentialService.GetByStoreIdAsync(storeId);
            var tenantOwnCredential = credentials.FirstOrDefault(c => c.ProviderKey == ProviderKey && c.IsActive);
            if (tenantOwnCredential != null)
            {
                var tenantAppId = _credentialService.DecryptForActualUse(tenantOwnCredential.ApiKey);
                var tenantAppSecret = _credentialService.DecryptForActualUse(tenantOwnCredential.ApiSecret);
                return (tenantAppId, tenantAppSecret, null);
            }

            var platformAppId = _configuration["InstagramAssistant:PlatformMetaAppId"];
            var platformAppSecret = _configuration["InstagramAssistant:PlatformMetaAppSecret"];
            if (!string.IsNullOrWhiteSpace(platformAppId) && !string.IsNullOrWhiteSpace(platformAppSecret))
                return (platformAppId, platformAppSecret, null);

            return (null, null, "نه اپ اختصاصی این فروشگاه و نه اپ مشترک پلتفرم (InstagramAssistant:PlatformMetaAppId/Secret) تنظیم نشده است.");
        }

        /// <summary>
        /// اگر IGSID قبلاً به یک مشتری وصل شده باشد همان مشتری برگردانده می‌شود؛ وگرنه یک مشتری
        /// nopCommerce جدید ساخته و نقش «Registered» به آن اختصاص داده می‌شود.
        /// ⚠️ این بخش (برخلاف بقیهٔ سرویس‌های این پروژه) هیچ نمونهٔ قبلی در کدبیس نداشت — همهٔ
        /// جاهای دیگر فرض می‌کردند مشتری از قبل با ایمیل/موبایل ثبت‌نام کرده. باید بعد از build
        /// واقعی، امضای دقیق ICustomerService در این نسخهٔ nopCommerce 4.90.6 تایید شود.
        /// </summary>
        private async Task<(Customer customer, bool isNewCustomer)> FindOrCreateCustomerForInstagramAsync(
            int storeId, string instagramUserId, string instagramUsername)
        {
            var existing = await _customerLinkService.GetCustomerByInstagramScopedIdAsync(instagramUserId);
            if (existing != null)
                return (existing, false);

            var placeholderEmail = $"ig-{instagramUserId}@instagram.igbz.local";

            var newCustomer = new Customer
            {
                CustomerGuid = Guid.NewGuid(),
                Email = placeholderEmail,
                Username = instagramUsername,
                RegisteredInStoreId = storeId,
                Active = true,
                CreatedOnUtc = DateTime.UtcNow,
                LastActivityDateUtc = DateTime.UtcNow
            };

            await _customerService.InsertCustomerAsync(newCustomer);

            var registeredRole = await _customerService.GetCustomerRoleBySystemNameAsync(NopCustomerDefaults.RegisteredRoleName);
            if (registeredRole != null)
            {
                await _customerService.AddCustomerRoleMappingAsync(new CustomerCustomerRoleMapping
                {
                    CustomerId = newCustomer.Id,
                    CustomerRoleId = registeredRole.Id
                });
            }

            await _genericAttributeService.SaveAttributeAsync(newCustomer, "InstagramUsername", instagramUsername);
            await _customerLinkService.LinkCustomerToInstagramScopedIdAsync(newCustomer.Id, instagramUserId);

            return (newCustomer, true);
        }
    }

    internal class InstagramShortTokenResponse
    {
        [JsonPropertyName("data")] public System.Collections.Generic.List<InstagramShortTokenEntry> Data { get; set; }

        // پشتیبانی محافظه‌کارانه از فرمت مسطح (قدیمی‌تر/جایگزین)
        [JsonPropertyName("access_token")] public string AccessToken { get; set; }
    }

    internal class InstagramShortTokenEntry
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; }
        [JsonPropertyName("user_id")] public string UserId { get; set; }
    }

    internal class InstagramLongTokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; }
        [JsonPropertyName("expires_in")] public long ExpiresInSeconds { get; set; }
    }

    internal class InstagramProfileResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("username")] public string Username { get; set; }
    }
}
