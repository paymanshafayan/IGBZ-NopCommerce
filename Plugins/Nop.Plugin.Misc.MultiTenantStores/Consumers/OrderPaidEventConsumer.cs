namespace Nop.Plugin.Misc.MultiTenantStores.Consumers
{
    using System;
    using System.Threading.Tasks;
    using Nop.Core.Domain.Orders;
    using Nop.Services.Events;
    using Nop.Services.Orders;
    using Nop.Services.Customers;
    using Nop.Services.Common;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// رویدادخوان پرداخت موفق فاکتور پلن مالتی‌تننت در سایت مادر
    /// </summary>
    public class OrderPaidEventConsumer : IConsumer<OrderPaidEvent>
    {
        private readonly IOrderService _orderService;
        private readonly ICustomerService _customerService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly ITenantProvisioningService _tenantProvisioningService;
        private readonly ITenantPlanService _tenantPlanService;

        public OrderPaidEventConsumer(
            IOrderService orderService,
            ICustomerService customerService,
            IGenericAttributeService genericAttributeService,
            ITenantProvisioningService tenantProvisioningService,
            ITenantPlanService tenantPlanService)
        {
            _orderService = orderService;
            _customerService = customerService;
            _genericAttributeService = genericAttributeService;
            _tenantProvisioningService = tenantProvisioningService;
            _tenantPlanService = tenantPlanService;
        }

        public async Task HandleEventAsync(OrderPaidEvent eventMessage)
        {
            if (eventMessage?.Order == null) return;

            var order = eventMessage.Order;
            var customer = await _customerService.GetCustomerByIdAsync(order.CustomerId);
            if (customer == null) return;

            var orderItems = await _orderService.GetOrderItemsAsync(order.Id);
            foreach (var item in orderItems)
            {
                // بررسی اینکه آیا این محصول متناظر با یک پلن چندفروشگاهی است
                var tenantPlan = await _tenantPlanService.GetPlanByProductIdAsync(item.ProductId);
                if (tenantPlan == null)
                    continue;

                // مسیر «ثبت‌نام از سایت مادر»: فروشگاه و اشتراکِ PendingPayment از قبل ساخته شده‌اند —
                // فقط باید فعال شوند. Provision مجدد باعث ساخت فروشگاه تکراری/خطا می‌شد.
                var existingSubscription = await _tenantPlanService.GetSubscriptionByOwnerAndPlanAsync(order.CustomerId, tenantPlan.Id);
                if (existingSubscription != null)
                {
                    await _tenantPlanService.ActivateSubscriptionAsync(existingSubscription.StoreId);
                    continue;
                }

                // مسیر «خرید مستقیم پلن از سایت مادر» (بدون ثبت‌نام قبلی): فروشگاه از صفر ساخته می‌شود.
                var requestedSubdomain = await _genericAttributeService.GetAttributeAsync<string>(customer, "PendingSubdomain") ?? $"store{order.Id}";
                var requestedStoreName = await _genericAttributeService.GetAttributeAsync<string>(customer, "PendingStoreName") ?? $"فروشگاه {customer.Email}";

                var provisionResult = await _tenantProvisioningService.ProvisionNewTenantStoreAsync(new ProvisionTenantRequest
                {
                    StoreName = requestedStoreName,
                    Subdomain = requestedSubdomain,
                    AdminEmail = customer.Email,
                    AdminPhoneNumber = customer.Phone,
                    PlanId = tenantPlan.Id
                });

                if (provisionResult.Success)
                {
                    // مدت اشتراک را از مبلغ واقعی پرداخت‌شده نسبت به قیمت‌های پلن استخراج کن
                    // (چرخهٔ صورتحساب در سفارش ذخیره نشده، ولی مبلغ آن را مشخص می‌کند)
                    var durationDays = order.OrderTotal >= tenantPlan.PriceYearly ? 365
                        : order.OrderTotal >= tenantPlan.PriceSixMonths ? 183
                        : 30;

                    // ساخت اشتراک Active + فعال‌سازی آنی دسترسی فروشگاه (پرداخت انجام شده)
                    await _tenantPlanService.EnsureSubscriptionActiveAsync(
                        provisionResult.StoreId, order.CustomerId, tenantPlan.Id, durationDays);
                }
            }
        }
    }
}