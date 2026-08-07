namespace Nop.Plugin.Misc.MultiTenantStores.Services
{
    using System;
    using System.Collections.Generic;
    using System.IdentityModel.Tokens.Jwt;
    using System.Security.Claims;
    using System.Text;
    using Microsoft.Extensions.Configuration;
    using Microsoft.IdentityModel.Tokens;

    /// <summary>
    /// صدور توکن دسترسی JWT برای احراز هویت اپ موبایل فلاتر — طبق تصمیم صریح سند معماری
    /// (ARCHITECTURE-NATIVE-v2.md: «tenantId از JWT Claim (موبایل) ... استخراج و توسط Middleware
    /// تزریق می‌شود»). تا پیش از این سرویس، هیچ‌جای پروژه توکن واقعی صادر نمی‌کرد؛ Controllerهای
    /// عمومی API (مثل AiCreditWalletController) صرفاً به IWorkContext.GetCurrentCustomerAsync
    /// متکی بودند که به کوکی نشست مرورگر وابسته است و برای یک کلاینت موبایل بومی قابل‌اتکا نیست.
    /// </summary>
    public interface IJwtTokenService
    {
        /// <summary>
        /// صدور توکن دسترسی حامل شناسهٔ مشتری (Claim استاندارد NameIdentifier) و شناسهٔ
        /// فروشگاه/تننت (Claim اختصاصی storeId). پیش‌فرض اعتبار: ۳۰ روز.
        /// </summary>
        string GenerateAccessToken(int customerId, int storeId, TimeSpan? validFor = null);
    }

    public class JwtTokenService : IJwtTokenService
    {
        private readonly IConfiguration _configuration;

        public JwtTokenService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string GenerateAccessToken(int customerId, int storeId, TimeSpan? validFor = null)
        {
            var signingSecret = _configuration["MultiTenantStores:JwtSigningSecret"];
            if (string.IsNullOrWhiteSpace(signingSecret))
                throw new InvalidOperationException(
                    "کلید MultiTenantStores:JwtSigningSecret در تنظیمات پیدا نشد. این مقدار باید از " +
                    "User Secrets یا متغیر محیطی تنظیم شود (حداقل ۳۲ کاراکتر تصادفی)، هرگز Hardcode نشود. " +
                    "همین کلید باید دقیقاً با کلیدی که در پیکربندی JwtBearer (بخش Configure JWT Bearer در " +
                    "NopStartup) استفاده می‌شود یکی باشد، وگرنه توکن‌های صادرشده هیچ‌وقت معتبر شناخته نمی‌شوند.");

            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingSecret));
            var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, customerId.ToString()),
                new("storeId", storeId.ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var expiresOnUtc = DateTime.UtcNow.Add(validFor ?? TimeSpan.FromDays(30));

            var token = new JwtSecurityToken(
                claims: claims,
                expires: expiresOnUtc,
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
