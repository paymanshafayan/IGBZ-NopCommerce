namespace Nop.Plugin.Api.Controllers.Public
{
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Configuration;
    using Nop.Core;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// نقطهٔ ورود واحد برای اپ فلاتر جهت نمایش «دو گزینهٔ ورود» به کاربر: پیج تجاری/کریتور
    /// اینستاگرام (InstagramLoginController) یا شمارهٔ موبایل + کد پیامکی (این کنترلر) — طبق
    /// محدودیت مستندشده در InstagramLoginController که Business Login for Instagram برای
    /// حساب‌های شخصی اصلاً کار نمی‌کند، موبایل مسیر واقعی جایگزین برای آن دسته از مشتریان است.
    /// </summary>
    [ApiController]
    [Route("api/public/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IStoreContext _storeContext;
        private readonly ITenantIntegrationCredentialService _credentialService;
        private readonly IPhoneOtpAuthService _phoneOtpAuthService;
        private readonly IConfiguration _configuration;

        public AuthController(
            IStoreContext storeContext,
            ITenantIntegrationCredentialService credentialService,
            IPhoneOtpAuthService phoneOtpAuthService,
            IConfiguration configuration)
        {
            _storeContext = storeContext;
            _credentialService = credentialService;
            _phoneOtpAuthService = phoneOtpAuthService;
            _configuration = configuration;
        }

        /// <summary>
        /// روش‌های ورود در دسترس این فروشگاه — اپ فلاتر بر اساس این پاسخ، دکمه‌های صفحهٔ ورود را
        /// می‌سازد (نه Hardcode در خودِ اپ، چون هر تننت ممکن است سرویس پیامک/اینستاگرام نداشته باشد).
        /// </summary>
        [HttpGet("login-options")]
        public async Task<IActionResult> GetLoginOptions()
        {
            var store = await _storeContext.GetCurrentStoreAsync();
            var credentials = await _credentialService.GetByStoreIdAsync(store.Id);

            var phoneOtpAvailable = credentials.Any(c => c.ProviderKey == "kavenegar" && c.IsActive);

            var hasTenantInstagramApp = credentials.Any(c => c.ProviderKey == "instagram.oauth" && c.IsActive);
            var hasPlatformInstagramApp =
                !string.IsNullOrWhiteSpace(_configuration["InstagramAssistant:PlatformMetaAppId"]) &&
                !string.IsNullOrWhiteSpace(_configuration["InstagramAssistant:PlatformMetaAppSecret"]);
            var instagramBusinessLoginAvailable = hasTenantInstagramApp || hasPlatformInstagramApp;

            return Ok(new
            {
                options = new object[]
                {
                    new
                    {
                        method = "instagram_business",
                        label = "ورود با پیج تجاری/کریتور اینستاگرام",
                        available = instagramBusinessLoginAvailable,
                        note = "فقط برای حساب‌های Business یا Creator اینستاگرام کار می‌کند — حساب‌های شخصی پشتیبانی نمی‌شوند."
                    },
                    new
                    {
                        method = "phone_otp",
                        label = "ورود با شماره موبایل",
                        available = phoneOtpAvailable,
                        note = "مناسب همهٔ کاربران، از جمله کسانی که حساب اینستاگرام شخصی دارند."
                    }
                }
            });
        }

        [HttpPost("phone/request-otp")]
        public async Task<IActionResult> RequestOtp([FromBody] PhoneOtpRequestDto dto)
        {
            var store = await _storeContext.GetCurrentStoreAsync();
            var (success, errorMessage) = await _phoneOtpAuthService.RequestOtpAsync(store.Id, dto?.PhoneNumber);

            if (!success)
                return BadRequest(new { success = false, message = errorMessage });

            return Ok(new { success = true, message = "کد ورود پیامک شد." });
        }

        [HttpPost("phone/verify-otp")]
        public async Task<IActionResult> VerifyOtp([FromBody] PhoneOtpVerifyDto dto)
        {
            var store = await _storeContext.GetCurrentStoreAsync();
            var result = await _phoneOtpAuthService.VerifyOtpAsync(store.Id, dto?.PhoneNumber, dto?.Code);

            if (!result.Success)
                return BadRequest(new { success = false, message = result.ErrorMessage });

            return Ok(new
            {
                success = true,
                accessToken = result.AccessToken,
                customerId = result.CustomerId,
                isNewCustomer = result.IsNewCustomer
            });
        }
    }

    public class PhoneOtpRequestDto
    {
        public string PhoneNumber { get; set; }
    }

    public class PhoneOtpVerifyDto
    {
        public string PhoneNumber { get; set; }
        public string Code { get; set; }
    }
}
