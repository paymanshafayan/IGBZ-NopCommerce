namespace Nop.Plugin.Misc.MultiTenantStores.Services
{
    using System;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Learning Management System (LMS) & ArvanCloud Dynamic Watermarked VOD Security (.NET 9)
    /// </summary>
    public interface ILmsAndVideoSecurityService
    {
        Task<SecureCourseVideoResult> GetWatermarkedCourseVideoUrlAsync(int courseId, int lessonId, int customerId, string userPhoneNumber, string userIpAddress, TimeSpan? validFor = null);
        bool ValidateSignedToken(string token, int courseId, int lessonId, int customerId, string userIpAddress);
    }

    public class LmsAndVideoSecurityService : ILmsAndVideoSecurityService
    {
        // در استقرار واقعی این کلید باید از appsettings.json / Key Vault خوانده شود، هرگز Hardcode نشود.
        private readonly byte[] _signingKey;

        public LmsAndVideoSecurityService(string hmacSigningSecret)
        {
            if (string.IsNullOrWhiteSpace(hmacSigningSecret))
                throw new ArgumentException("کلید امضای HMAC برای صدور توکن ویدیوی امن الزامی است.", nameof(hmacSigningSecret));

            _signingKey = Encoding.UTF8.GetBytes(hmacSigningSecret);
        }

        /// <summary>
        /// تولید یک لینک پخش امن با توکن امضاشدهٔ HMAC-SHA256 دارای انقضا و مقید به IP کاربر؛
        /// جعل یا دستکاری این توکن بدون دانستن کلید امضا عملاً غیرممکن است.
        /// </summary>
        public async Task<SecureCourseVideoResult> GetWatermarkedCourseVideoUrlAsync(
            int courseId, int lessonId, int customerId, string userPhoneNumber, string userIpAddress, TimeSpan? validFor = null)
        {
            var expiresAtUnix = DateTimeOffset.UtcNow.Add(validFor ?? TimeSpan.FromHours(4)).ToUnixTimeSeconds();
            var payload = $"{courseId}.{lessonId}.{customerId}.{userIpAddress}.{expiresAtUnix}";
            var signature = ComputeSignature(payload);

            var secureToken = $"{Base64UrlEncode(payload)}.{signature}";
            var embedVideoUrl =
                $"https://vod.arvancloud.ir/embed/{courseId}/{lessonId}" +
                $"?token={Uri.EscapeDataString(secureToken)}" +
                $"&wm_text={Uri.EscapeDataString(MaskPhoneForWatermark(userPhoneNumber))}";

            return await Task.FromResult(new SecureCourseVideoResult
            {
                IsSuccess = true,
                EmbedPlayerUrl = embedVideoUrl,
                SignedToken = secureToken,
                ExpiresOnUtc = DateTimeOffset.FromUnixTimeSeconds(expiresAtUnix).UtcDateTime,
                WatermarkPositionMode = "DYNAMIC_MOVING_TEXT",
                Message = "لینک ویدیو با توکن امضاشده و واترمارک متحرک شماره همراه صادر شد."
            });
        }

        /// <summary>
        /// اعتبارسنجی توکن دریافتی از سمت پلیر ویدیو پیش از اجازه پخش (باید در Middleware/Controller
        /// سرویس VOD فراخوانی شود).
        /// </summary>
        public bool ValidateSignedToken(string token, int courseId, int lessonId, int customerId, string userIpAddress)
        {
            if (string.IsNullOrWhiteSpace(token) || !token.Contains('.'))
                return false;

            var parts = token.Split('.', 2);
            if (parts.Length != 2)
                return false;

            var payload = Base64UrlDecode(parts[0]);
            var providedSignature = parts[1];
            var expectedSignature = ComputeSignature(payload);

            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(providedSignature),
                    Encoding.UTF8.GetBytes(expectedSignature)))
                return false;

            var segments = payload.Split('.');
            if (segments.Length != 5)
                return false;

            var isMatchingContext =
                segments[0] == courseId.ToString() &&
                segments[1] == lessonId.ToString() &&
                segments[2] == customerId.ToString() &&
                segments[3] == userIpAddress;

            var notExpired = long.TryParse(segments[4], out var expiresAtUnix)
                && DateTimeOffset.UtcNow.ToUnixTimeSeconds() <= expiresAtUnix;

            return isMatchingContext && notExpired;
        }

        private string ComputeSignature(string payload)
        {
            using var hmac = new HMACSHA256(_signingKey);
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToBase64String(hash).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        private static string Base64UrlEncode(string input) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(input)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        private static string Base64UrlDecode(string input)
        {
            var s = input.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Encoding.UTF8.GetString(Convert.FromBase64String(s));
        }

        private static string MaskPhoneForWatermark(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone) || phone.Length < 4)
                return phone ?? string.Empty;
            return phone.Substring(0, phone.Length - 4) + "****";
        }
    }

    public class SecureCourseVideoResult
    {
        public bool IsSuccess { get; set; }
        public string EmbedPlayerUrl { get; set; }
        public string SignedToken { get; set; }
        public DateTime ExpiresOnUtc { get; set; }
        public string WatermarkPositionMode { get; set; }
        public string Message { get; set; }
    }
}
