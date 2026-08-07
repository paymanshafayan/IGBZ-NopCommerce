namespace Nop.Plugin.Misc.MultiTenantStores.Consumers
{
    using System.Threading.Tasks;
    using Nop.Core.Domain.Orders;
    using Nop.Services.Events;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// به‌محض پرداخت موفق هر سفارش، اگر خریدار از طریق یک معرف وارد شده باشد، کمیسیون واقعی
    /// در دفترکل Affiliate ثبت می‌شود (طبق راهنمای Affiliate Marketing، بند «Event Consumer برای
    /// اتمام سفارش»). این پیاده‌سازی جایگزین نسخهٔ قبلی <c>ProcessAffiliateCommissionAsync</c> است
    /// که فقط عدد را محاسبه می‌کرد و هیچ رکوردی درج نمی‌کرد.
    /// </summary>
    public class AffiliateCommissionOrderConsumer : IConsumer<OrderPaidEvent>
    {
        private const decimal DefaultCommissionPercent = 5;

        private readonly IAffiliateMarketingService _affiliateService;

        public AffiliateCommissionOrderConsumer(IAffiliateMarketingService affiliateService)
        {
            _affiliateService = affiliateService;
        }

        public async Task HandleEventAsync(OrderPaidEvent eventMessage)
        {
            var order = eventMessage.Order;
            if (order == null) return;

            await _affiliateService.ProcessOrderCommissionAsync(
                order.StoreId, order.Id, order.CustomerId, order.OrderTotal, DefaultCommissionPercent);
        }
    }
}
