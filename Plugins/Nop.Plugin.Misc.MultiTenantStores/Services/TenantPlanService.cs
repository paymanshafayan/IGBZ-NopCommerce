namespace Nop.Plugin.Misc.MultiTenantStores.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Nop.Core.Domain.Messages;
    using Nop.Core.Domain.Orders;
    using Nop.Data;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;
    using Nop.Services.Catalog;
    using Nop.Services.Customers;
    using Nop.Services.Messages;
    using Nop.Services.Orders;

    public interface ITenantPlanService
    {
        Task<IList<TenantPlan>> GetAllActivePlansAsync();
        Task<IList<TenantPlan>> GetAllPlansAsync();
        Task<TenantPlan> GetPlanByIdAsync(int planId);
        Task<TenantPlan> GetPlanByProductIdAsync(int productId);
        Task InsertPlanAsync(TenantPlan plan);
        Task UpdatePlanAsync(TenantPlan plan);
        Task DeletePlanAsync(int planId);
        Task<TenantStoreSubscription> GetSubscriptionByStoreIdAsync(int storeId);

        /// <summary>
        /// پلن فعال فعلی یک فروشگاه را برمی‌گرداند (برای Gate کردن قابلیت‌های مخصوص پلن، مثل
        /// دستیار اینستاگرام Pro) — اگر اشتراک فعال/آزمایشی نبود، null برمی‌گرداند.
        /// </summary>
        Task<TenantPlan> GetActivePlanForStoreAsync(int storeId);
        Task<int> CreateSubscriptionOrderAsync(int storeId, int customerId, int planId, BillingCycle billingCycle);
        Task<IList<TenantStoreSubscription>> GetSubscriptionsExpiringInDaysAsync(int days);
        Task<IList<TenantStoreSubscription>> GetExpiredSubscriptionsAsync();
        Task SendExpiryWarningNotificationAsync(int storeId, int daysRemaining);
    }

    public class TenantPlanService : ITenantPlanService
    {
        public const int MasterPlatformStoreId = 1;

        private readonly IRepository<TenantPlan> _planRepository;
        private readonly IRepository<TenantStoreSubscription> _subscriptionRepository;
        private readonly IOrderService _orderService;
        private readonly IProductService _productService;
        private readonly ICustomerService _customerService;
        private readonly IQueuedEmailService _queuedEmailService;
        private readonly IEmailAccountService _emailAccountService;
        private readonly EmailAccountSettings _emailAccountSettings;

        public TenantPlanService(
            IRepository<TenantPlan> planRepository,
            IRepository<TenantStoreSubscription> subscriptionRepository,
            IOrderService orderService,
            IProductService productService,
            ICustomerService customerService,
            IQueuedEmailService queuedEmailService,
            IEmailAccountService emailAccountService,
            EmailAccountSettings emailAccountSettings)
        {
            _planRepository = planRepository;
            _subscriptionRepository = subscriptionRepository;
            _orderService = orderService;
            _productService = productService;
            _customerService = customerService;
            _queuedEmailService = queuedEmailService;
            _emailAccountService = emailAccountService;
            _emailAccountSettings = emailAccountSettings;
        }

        public async Task<IList<TenantPlan>> GetAllActivePlansAsync()
        {
            return await _planRepository.GetAllAsync(q => q.Where(p => p.IsActive).OrderBy(p => p.DisplayOrder));
        }

        public async Task<IList<TenantPlan>> GetAllPlansAsync()
        {
            return await _planRepository.GetAllAsync(q => q.OrderBy(p => p.DisplayOrder));
        }

        public async Task<TenantPlan> GetPlanByIdAsync(int planId)
        {
            return await _planRepository.GetByIdAsync(planId);
        }

        public async Task<TenantPlan> GetPlanByProductIdAsync(int productId)
        {
            var all = await _planRepository.GetAllAsync(q => q.Where(p => p.LinkedProductId == productId && p.IsActive));
            return all.FirstOrDefault();
        }

        public async Task<TenantPlan> GetActivePlanForStoreAsync(int storeId)
        {
            var subscription = await GetSubscriptionByStoreIdAsync(storeId);
            if (subscription == null)
                return null;

            var isUsable = subscription.Status == SubscriptionStatus.Active
                || (subscription.Status == SubscriptionStatus.Trial
                    && subscription.TrialEndDateUtc.HasValue
                    && subscription.TrialEndDateUtc.Value > DateTime.UtcNow);

            return isUsable ? await GetPlanByIdAsync(subscription.TenantPlanId) : null;
        }
        {
            await _planRepository.InsertAsync(plan);
        }

        public async Task UpdatePlanAsync(TenantPlan plan)
        {
            await _planRepository.UpdateAsync(plan);
        }

        public async Task DeletePlanAsync(int planId)
        {
            var plan = await _planRepository.GetByIdAsync(planId);
            if (plan != null)
                await _planRepository.DeleteAsync(plan);
        }

        public async Task<TenantStoreSubscription> GetSubscriptionByStoreIdAsync(int storeId)
        {
            var all = await _subscriptionRepository.GetAllAsync(q =>
                q.Where(s => s.StoreId == storeId).OrderByDescending(s => s.CreatedOnUtc));
            return all.FirstOrDefault();
        }

        /// <summary>
        /// ثبت سفارش واقعی تمدید/ارتقای پلن به‌عنوان یک Order در nopCommerce، برای محصول متناظر با پلن،
        /// روی سایت مادر (Master Platform Store). این سفارش سپس توسط درگاه پرداخت (ParbadPaymentService)
        /// و رویداد OrderPaidEvent -> OrderPaidEventConsumer تکمیل می‌شود.
        /// </summary>
        /// <summary>
        /// برای پلن‌های آزمایشی (TrialDurationDays &gt; 0) هیچ سفارش/پرداختی ساخته نمی‌شود — اشتراک
        /// مستقیماً با وضعیت Trial فعال می‌شود و ۰ برمی‌گردد (یعنی «سفارشی برای پرداخت وجود ندارد»).
        /// </summary>
        public async Task<int> CreateSubscriptionOrderAsync(int storeId, int customerId, int planId, BillingCycle billingCycle)
        {
            var plan = await GetPlanByIdAsync(planId);
            if (plan == null)
                throw new InvalidOperationException($"پلن با شناسه {planId} یافت نشد.");

            if (customerId <= 0)
                throw new ArgumentException("شناسهٔ مشتری معتبر برای صدور فاکتور اشتراک الزامی است.", nameof(customerId));

            var existingSub = await GetSubscriptionByStoreIdAsync(storeId);

            if (plan.TrialDurationDays > 0)
            {
                if (existingSub == null)
                {
                    await _subscriptionRepository.InsertAsync(new TenantStoreSubscription
                    {
                        StoreId = storeId,
                        TenantPlanId = plan.Id,
                        OwnerCustomerId = customerId,
                        Status = SubscriptionStatus.Trial,
                        StartDateUtc = DateTime.UtcNow,
                        TrialEndDateUtc = DateTime.UtcNow.AddDays(plan.TrialDurationDays),
                        NextBillingDateUtc = DateTime.UtcNow.AddDays(plan.TrialDurationDays),
                        AutoRenew = false,
                        CreatedOnUtc = DateTime.UtcNow,
                        UpdatedOnUtc = DateTime.UtcNow
                    });
                }

                return 0;
            }

            var amount = billingCycle switch
            {
                BillingCycle.SixMonths => plan.PriceSixMonths,
                BillingCycle.Yearly => plan.PriceYearly,
                _ => plan.PriceMonthly
            };

            var durationDays = billingCycle switch
            {
                BillingCycle.SixMonths => 183,
                BillingCycle.Yearly => 365,
                _ => 30
            };

            var order = new Order
            {
                StoreId = MasterPlatformStoreId,
                CustomerId = customerId,
                OrderGuid = Guid.NewGuid(),
                OrderTotal = amount,
                OrderSubtotalInclTax = amount,
                OrderSubtotalExclTax = amount,
                PaymentStatusId = (int)PaymentStatus.Pending,
                OrderStatusId = (int)OrderStatus.Pending,
                CustomOrderNumber = string.Empty,
                CreatedOnUtc = DateTime.UtcNow,
                CustomerCurrencyCode = "IRT"
            };

            await _orderService.InsertOrderAsync(order);
            order.CustomOrderNumber = order.Id.ToString();
            await _orderService.UpdateOrderAsync(order);

            if (existingSub == null)
            {
                await _subscriptionRepository.InsertAsync(new TenantStoreSubscription
                {
                    StoreId = storeId,
                    TenantPlanId = plan.Id,
                    OwnerCustomerId = customerId,
                    Status = SubscriptionStatus.PendingPayment,
                    StartDateUtc = DateTime.UtcNow,
                    NextBillingDateUtc = DateTime.UtcNow.AddDays(durationDays),
                    AutoRenew = true,
                    CreatedOnUtc = DateTime.UtcNow,
                    UpdatedOnUtc = DateTime.UtcNow
                });
            }
            else
            {
                existingSub.TenantPlanId = plan.Id;
                existingSub.Status = SubscriptionStatus.PendingPayment;
                existingSub.UpdatedOnUtc = DateTime.UtcNow;
                await _subscriptionRepository.UpdateAsync(existingSub);
            }

            return order.Id;
        }

        public async Task<IList<TenantStoreSubscription>> GetSubscriptionsExpiringInDaysAsync(int days)
        {
            var threshold = DateTime.UtcNow.AddDays(days);
            return await _subscriptionRepository.GetAllAsync(q =>
                q.Where(s => s.Status == SubscriptionStatus.Active
                    && s.NextBillingDateUtc <= threshold
                    && s.NextBillingDateUtc > DateTime.UtcNow));
        }

        public async Task<IList<TenantStoreSubscription>> GetExpiredSubscriptionsAsync()
        {
            return await _subscriptionRepository.GetAllAsync(q =>
                q.Where(s => (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue)
                    && s.NextBillingDateUtc < DateTime.UtcNow
                    && !s.AutoRenew));
        }

        public async Task SendExpiryWarningNotificationAsync(int storeId, int daysRemaining)
        {
            var subscription = await GetSubscriptionByStoreIdAsync(storeId);
            if (subscription == null || subscription.OwnerCustomerId <= 0)
                return;

            var ownerCustomer = await _customerService.GetCustomerByIdAsync(subscription.OwnerCustomerId);
            if (ownerCustomer == null || string.IsNullOrWhiteSpace(ownerCustomer.Email))
                return;

            var emailAccount = await _emailAccountService.GetEmailAccountByIdAsync(_emailAccountSettings.DefaultEmailAccountId);
            if (emailAccount == null)
                return; // بدون حساب ایمیل پیش‌فرض پیکربندی‌شده، ارسال واقعی ممکن نیست — به‌جای شبیه‌سازی موفقیت، هیچ کاری انجام نمی‌شود

            // پیاده‌سازی مرجع: صف‌بندی واقعی ایمیل تراکنشی از طریق IQueuedEmailService.
            // در استقرار واقعی، این متد باید هم‌زمان به IntegrationProvider نوع OTP/پیامک
            // (بخش ۷ سند معماری) نیز متصل شود تا هشدار پیامکی هم ارسال گردد.
            await _queuedEmailService.InsertQueuedEmailAsync(new QueuedEmail
            {
                PriorityId = (int)QueuedEmailPriority.High,
                From = emailAccount.Email,
                FromName = emailAccount.DisplayName,
                To = ownerCustomer.Email,
                ToName = ownerCustomer.Email,
                Subject = $"اشتراک فروشگاه شما تا {daysRemaining} روز دیگر منقضی می‌شود",
                Body = $"اشتراک فروشگاه شماره {storeId} به‌زودی منقضی خواهد شد. برای جلوگیری از تعلیق سرویس، لطفاً نسبت به تمدید اقدام کنید.",
                CreatedOnUtc = DateTime.UtcNow,
                EmailAccountId = emailAccount.Id
            });
        }
    }
}
