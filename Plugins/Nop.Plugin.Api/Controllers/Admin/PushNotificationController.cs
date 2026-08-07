namespace Nop.Plugin.Api.Controllers.Admin
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Plugin.Api.Services;

    [ApiController]
    [Route("api/admin/notifications")]
    public class PushNotificationController : AuthorizedTenantOwnerApiController
    {
        private readonly IFcmService _fcmService;

        public PushNotificationController(
            IWorkContext workContext,
            IStoreContext storeContext,
            IFcmService fcmService) : base(workContext, storeContext)
        {
            _fcmService = fcmService;
        }

        [HttpPost("register-device-token")]
        public async Task<IActionResult> RegisterDeviceToken([FromBody] RegisterFcmTokenDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.FcmToken))
                return BadRequest(new { message = "توکن FCM معتبر نیست." });

            var store = await GetAuthorizedStoreAsync();
            var adminUser = await GetCurrentCustomerAsync();

            await _fcmService.RegisterAdminTokenAsync(adminUser.Id, store.Id, dto.FcmToken, dto.DeviceName);

            return Ok(new { success = true, message = "توکن دستگاه با موفقیت برای دریافت پیام‌ها ثبت شد." });
        }

        [HttpPost("send-test")]
        public async Task<IActionResult> SendTestNotification([FromBody] SendPushRequestDto dto)
        {
            var store = await GetAuthorizedStoreAsync();

            var result = await _fcmService.SendNotificationToStoreAdminsAsync(
                storeId: store.Id,
                title: dto.Title ?? $"هشدار فروشگاه {store.Name}",
                body: dto.Body ?? "یک سفارش جدید در سیستم ثبت شد.",
                dataPayload: new System.Collections.Generic.Dictionary<string, string>
                {
                    { "type", dto.Type ?? "OrderCreated" },
                    { "storeId", store.Id.ToString() }
                }
            );

            return Ok(new { success = result.Success, deliveredCount = result.DeliveredCount });
        }
    }

    public class RegisterFcmTokenDto
    {
        public string FcmToken { get; set; }
        public string DeviceName { get; set; }
    }

    public class SendPushRequestDto
    {
        public string Title { get; set; }
        public string Body { get; set; }
        public string Type { get; set; }
    }
}