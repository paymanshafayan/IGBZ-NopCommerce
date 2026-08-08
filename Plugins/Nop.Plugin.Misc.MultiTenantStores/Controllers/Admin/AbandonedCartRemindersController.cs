namespace Nop.Plugin.Misc.MultiTenantStores.Controllers.Admin
{
    using System.Linq;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Web.Framework;
    using Nop.Web.Framework.Controllers;
    using Nop.Web.Framework.Mvc.Filters;
    using Nop.Services.Security;
    using Nop.Core.Domain.Customers;
    using Nop.Services.Common;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// اولین نقطهٔ ورود واقعی برای GamificationAndAffiliateService.TriggerAbandonedCartSmsRemindersAsync
    /// — قبلاً این سرویس نوشته شده بود ولی هیچ Controllerی صداش نمی‌زد. ارسال واقعی پیامک از طریق
    /// همان سرویس کاوه‌نگار (کلید Provider = kavenegar) که در PhoneOtpAuthService استفاده می‌شود.
    /// </summary>
    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [AutoValidateAntiforgeryToken]
    [ServiceFilter(typeof(Infrastructure.Filters.TenantAdminScopeFilter))]
    public class AbandonedCartRemindersController : BasePluginController
    {
        private readonly IPermissionService _permissionService;
        private readonly IGamificationAndAffiliateService _gamificationService;
        private readonly ITenantIntegrationCredentialService _credentialService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IHttpClientFactory _httpClientFactory;

        public AbandonedCartRemindersController(
            IPermissionService permissionService,
            IGamificationAndAffiliateService gamificationService,
            ITenantIntegrationCredentialService credentialService,
            IGenericAttributeService genericAttributeService,
            IHttpClientFactory httpClientFactory)
        {
            _permissionService = permissionService;
            _gamificationService = gamificationService;
            _credentialService = credentialService;
            _genericAttributeService = genericAttributeService;
            _httpClientFactory = httpClientFactory;
        }

        [HttpPost]
        public async Task<IActionResult> Trigger(int storeId, int abandonMinutesThreshold = 60)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
                return Json(new { success = false, message = "دسترسی رد شد." });

            var credentials = await _credentialService.GetByStoreIdAsync(storeId);
            var credential = credentials.FirstOrDefault(c => c.ProviderKey == "kavenegar" && c.IsActive);
            if (credential == null)
                return Json(new { success = false, message = "سرویس پیامک (کاوه‌نگار) برای این فروشگاه تنظیم نشده است." });

            var apiKey = _credentialService.DecryptForActualUse(credential.ApiKey);
            var httpClient = _httpClientFactory.CreateClient("KavenegarSms");

            var sentCount = await _gamificationService.TriggerAbandonedCartSmsRemindersAsync(
                storeId,
                async (customer, cartItem) => await SendReminderSmsAsync(httpClient, apiKey, customer),
                abandonMinutesThreshold);

            return Json(new { success = true, sentCount });
        }

        private async Task<bool> SendReminderSmsAsync(HttpClient httpClient, string apiKey, Customer customer)
        {
            // در nopCommerce 4.90.6 شمارهٔ موبایل فیلد مستقیم Customer.Phone است، نه GenericAttribute
            var phone = customer.Phone;
            if (string.IsNullOrWhiteSpace(phone))
                return false;

            try
            {
                var message = System.Uri.EscapeDataString("سبد خریدت منتظرته! برای تکمیل خرید به فروشگاه سر بزن.");
                var sendUrl = $"https://api.kavenegar.com/v1/{apiKey}/sms/send.json?receptor={System.Uri.EscapeDataString(phone)}&message={message}";
                var response = await httpClient.PostAsync(sendUrl, null);
                return response.IsSuccessStatusCode;
            }
            catch (System.Exception)
            {
                return false;
            }
        }
    }
}
