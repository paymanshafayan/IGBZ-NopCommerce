namespace Nop.Plugin.Misc.MultiTenantStores.Controllers.Admin
{
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Web.Framework;
    using Nop.Web.Framework.Controllers;
    using Nop.Web.Framework.Mvc.Filters;
    using Nop.Services.Security;
    using Nop.Services.Orders;
    using Nop.Services.Common;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// اولین نقطهٔ ورود واقعی HTTP برای LogisticsAndShippingService — قبلاً این سرویس (دسته‌بندی
    /// مسیر ارسال + ثبت واقعی مرسوله در تاپین) کامل نوشته شده بود ولی هیچ Controllerی صداش نمی‌زد.
    /// </summary>
    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [AutoValidateAntiforgeryToken]
    [ServiceFilter(typeof(Infrastructure.Filters.TenantAdminScopeFilter))]
    public class ShipmentController : BasePluginController
    {
        private const string TapinProviderKey = "tapin";

        private readonly IPermissionService _permissionService;
        private readonly IOrderService _orderService;
        private readonly IAddressService _addressService;
        private readonly ITenantIntegrationCredentialService _credentialService;
        private readonly ILogisticsAndShippingService _logisticsService;

        public ShipmentController(
            IPermissionService permissionService,
            IOrderService orderService,
            IAddressService addressService,
            ITenantIntegrationCredentialService credentialService,
            ILogisticsAndShippingService logisticsService)
        {
            _permissionService = permissionService;
            _orderService = orderService;
            _addressService = addressService;
            _credentialService = credentialService;
            _logisticsService = logisticsService;
        }

        /// <summary>پیشنهاد مسیر/شرکت حمل‌ونقل بر اساس وزن و شهر مقصد — برای نمایش پیش از تایید ثبت مرسوله.</summary>
        [HttpGet]
        public IActionResult SuggestRoute(decimal weightKg, string destinationCity, bool isExpressNeeded = false)
        {
            var result = _logisticsService.CategorizeShipmentRoute(weightKg, destinationCity, isExpressNeeded);
            return Json(result);
        }

        /// <summary>ثبت واقعی مرسولهٔ سفارش در سامانهٔ تاپین و برگرداندن کد پیگیری واقعی + PIN تحویل.</summary>
        [HttpPost]
        public async Task<IActionResult> RegisterShipment(int orderId, bool isCod)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS))
                return Json(new { success = false, message = "دسترسی رد شد." });

            var order = await _orderService.GetOrderByIdAsync(orderId);
            if (order == null)
                return Json(new { success = false, message = "سفارش یافت نشد." });

            if (!order.ShippingAddressId.HasValue)
                return Json(new { success = false, message = "این سفارش آدرس ارسال ندارد (احتمالاً سفارش دیجیتال/بدون ارسال است)." });

            var address = await _addressService.GetAddressByIdAsync(order.ShippingAddressId.Value);
            if (address == null)
                return Json(new { success = false, message = "آدرس ارسال سفارش یافت نشد." });

            var credentials = await _credentialService.GetByStoreIdAsync(order.StoreId);
            var credential = credentials.FirstOrDefault(c => c.ProviderKey == TapinProviderKey && c.IsActive);
            if (credential == null)
                return Json(new { success = false, message = "اتصال تاپین برای این فروشگاه تنظیم نشده است." });

            var apiKey = _credentialService.DecryptForActualUse(credential.ApiKey);
            var recipientAddress = $"{address.City}، {address.Address1}";

            var result = await _logisticsService.RegisterTapinPostShipmentAsync(
                apiKey, order.Id, recipientAddress, address.PhoneNumber, isCod);

            return Json(new
            {
                success = result.IsSuccess,
                message = result.Message,
                postTrackingCode = result.PostTrackingCode,
                deliveryPin = result.DeliveryPin,
                barcodeImageUrl = result.BarcodeImageUrl
            });
        }
    }
}
