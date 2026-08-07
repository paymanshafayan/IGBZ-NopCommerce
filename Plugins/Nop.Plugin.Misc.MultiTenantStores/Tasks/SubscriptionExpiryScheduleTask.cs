namespace Nop.Plugin.Misc.MultiTenantStores.Tasks
{
    using System;
    using System.Threading.Tasks;
    using Nop.Services.ScheduleTasks;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// جاب خودکار بررسی روزانه انقضای اشتراک فروشگاه‌های چندمستاجره
    /// </summary>
    public class SubscriptionExpiryScheduleTask : IScheduleTask
    {
        private readonly ITenantPlanService _tenantPlanService;
        private readonly ITenantProvisioningService _tenantProvisioningService;

        public SubscriptionExpiryScheduleTask(
            ITenantPlanService tenantPlanService,
            ITenantProvisioningService tenantProvisioningService)
        {
            _tenantPlanService = tenantPlanService;
            _tenantProvisioningService = tenantProvisioningService;
        }

        public async Task ExecuteAsync()
        {
            // ۱. شناسایی اشتراک‌هایی که ۳ روز به انقضا دارند جهت ارسال SMS
            var expiringSoon = await _tenantPlanService.GetSubscriptionsExpiringInDaysAsync(3);
            foreach (var sub in expiringSoon)
            {
                // ارسال نوتیفیکیشن هشدار انقضا
                await _tenantPlanService.SendExpiryWarningNotificationAsync(sub.StoreId, 3);
            }

            // ۲. شناسایی و تعلیق فروشگاه‌های منقضی‌شده که تمدید نکرده‌اند
            var expiredSubs = await _tenantPlanService.GetExpiredSubscriptionsAsync();
            foreach (var expired in expiredSubs)
            {
                await _tenantProvisioningService.SuspendTenantStoreAsync(
                    expired.StoreId, 
                    "انقضای مهلت اشتراک و عدم پرداخت فاکتور تمدید"
                );
            }
        }
    }
}