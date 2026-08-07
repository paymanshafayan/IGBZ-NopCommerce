namespace Nop.Plugin.Misc.MultiTenantStores.Consumers
{
    using System;
    using System.Threading.Tasks;
    using Nop.Core.Domain.Orders;
    using Nop.Services.Events;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// به‌ازای هر سفارش پرداخت‌شده، پاداش «دستیار هوشمند تولید محتوا» را خودکار به کیف‌پول واحد
    /// مشتری واریز می‌کند (نیازمندی #۱۲: «با هر خرید شارژ می‌شود»). قبلاً این پاداش در واحد انتزاعی
    /// «Credit» محاسبه می‌شد؛ حالا که کیف‌پول یکپارچه و صرفاً تومانی است، پاداش هم مستقیماً
    /// به‌صورت درصدی از مبلغ سفارش به تومان محاسبه می‌شود.
    /// </summary>
    public class AiCreditOrderBonusConsumer : IConsumer<OrderPaidEvent>
    {
        // در نسخهٔ کامل باید این نرخ به‌صورت تنظیمات قابل‌ویرایش هر تننت باشد (TenantPlan یا Setting).
        private const decimal OrderAiBonusPercent = 2m;

        private readonly IWalletService _walletService;

        public AiCreditOrderBonusConsumer(IWalletService walletService)
        {
            _walletService = walletService;
        }

        public async Task HandleEventAsync(OrderPaidEvent eventMessage)
        {
            var order = eventMessage.Order;
            if (order == null) return;

            var bonusToman = Math.Round((order.OrderTotal * OrderAiBonusPercent) / 100, 0);
            if (bonusToman <= 0) return;

            await _walletService.CreditAsync(
                order.CustomerId, order.StoreId, bonusToman, WalletTransactionReason.OrderAiFeatureBonus, $"order-ai-bonus-{order.Id}");
        }
    }
}
