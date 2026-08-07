namespace Nop.Plugin.Misc.MultiTenantStores.Services
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Nop.Core.Domain.Customers;
    using Nop.Core.Domain.Discounts;
    using Nop.Core.Domain.Orders;
    using Nop.Data;
    using Nop.Services.Customers;
    using Nop.Services.Common;
    using Nop.Services.Discounts;

    /// <summary>
    /// چرخ‌وفلک جایزه و یادآوری سبد خرید رهاشده.
    /// ⚠️ توجه: این سرویس قبلاً یک متد `ProcessAffiliateCommissionAsync` جداگانه هم داشت که با
    /// سیستم واقعی و متصل Affiliate (AffiliateMarketingService + کیف‌پول واحد WalletService) تداخل
    /// مفهومی داشت و اصلاً هرگز صدا زده نمی‌شد (فقط کارمزد را محاسبه و «صف‌بندی» می‌کرد، بدون این‌که
    /// واقعاً جایی واریز شود). آن متد حذف شد — مسیر واحد و درست برای کمیسیون Affiliate همان
    /// AffiliateMarketingService.ProcessOrderCommissionAsync (متصل به OrderPaidEvent) است.
    /// </summary>
    public interface IGamificationAndAffiliateService
    {
        Task<SpinWheelRewardResult> SpinWheelOfFortuneAsync(int customerId, int storeId);
        Task<int> TriggerAbandonedCartSmsRemindersAsync(int storeId, Func<Customer, ShoppingCartItem, Task<bool>> sendSmsCallback, int abandonMinutesThreshold = 60);
    }

    public class GamificationAndAffiliateService : IGamificationAndAffiliateService
    {
        private const string LastSpinAttributeKey = "LastWheelSpinDateUtc";
        private static readonly TimeSpan SpinCooldown = TimeSpan.FromHours(24);

        private readonly IRepository<ShoppingCartItem> _shoppingCartRepository;
        private readonly ICustomerService _customerService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IDiscountService _discountService;

        public GamificationAndAffiliateService(
            IRepository<ShoppingCartItem> shoppingCartRepository,
            ICustomerService customerService,
            IGenericAttributeService genericAttributeService,
            IDiscountService discountService)
        {
            _shoppingCartRepository = shoppingCartRepository;
            _customerService = customerService;
            _genericAttributeService = genericAttributeService;
            _discountService = discountService;
        }

        /// <summary>
        /// چرخش چرخ‌وفلک شانس — حداکثر یک‌بار در ۲۴ ساعت برای هر مشتری (وگرنه امکان چرخش پی‌درپی
        /// برای گرفتن چندین کد تخفیف نامحدود وجود داشت). جایزهٔ «تخفیف» واقعاً یک Discount یک‌بارمصرف
        /// nopCommerce می‌سازد (نه فقط یک رشتهٔ متنی که قبلاً هرگز در جایی قابل‌استفاده نبود).
        /// </summary>
        public async Task<SpinWheelRewardResult> SpinWheelOfFortuneAsync(int customerId, int storeId)
        {
            var customer = await _customerService.GetCustomerByIdAsync(customerId);
            if (customer == null)
                return new SpinWheelRewardResult { IsSuccess = false, Message = "مشتری یافت نشد." };

            var lastSpin = await _genericAttributeService.GetAttributeAsync<DateTime?>(customer, LastSpinAttributeKey);
            if (lastSpin.HasValue && DateTime.UtcNow - lastSpin.Value < SpinCooldown)
            {
                var remaining = SpinCooldown - (DateTime.UtcNow - lastSpin.Value);
                return new SpinWheelRewardResult
                {
                    IsSuccess = false,
                    Message = $"هر ۲۴ ساعت فقط یک‌بار می‌توانید چرخ‌وفلک را بچرخانید. حدود {(int)remaining.TotalHours} ساعت دیگر دوباره امتحان کنید."
                };
            }

            var rewardOptions = new[]
            {
                (Title: "تخفیف ۱۰٪", Percent: 10m),
                (Title: "تخفیف ۲۰٪", Percent: 20m),
                (Title: "تخفیف ۵٪", Percent: 5m)
            };
            var selected = rewardOptions[RandomNumberGenerator() % rewardOptions.Length];
            var couponCode = $"SPIN-{customerId}-{DateTime.UtcNow:yyyyMMddHHmmss}";

            await _discountService.InsertDiscountAsync(new Discount
            {
                Name = $"جایزهٔ چرخ‌وفلک - مشتری {customerId}",
                DiscountTypeId = (int)DiscountType.AssignedToOrderTotal,
                UsePercentage = true,
                DiscountPercentage = selected.Percent,
                RequiresCouponCode = true,
                CouponCode = couponCode,
                IsCumulative = false,
                DiscountLimitationId = (int)DiscountLimitationType.NTimesOnly,
                LimitationTimes = 1,
                IsActive = true
            });

            await _genericAttributeService.SaveAttributeAsync(customer, LastSpinAttributeKey, DateTime.UtcNow);

            return new SpinWheelRewardResult
            {
                IsSuccess = true,
                RewardTitle = selected.Title,
                DiscountCode = couponCode,
                Message = $"مبارک است! شما برنده {selected.Title} شدید. کد: {couponCode}"
            };
        }

        /// <summary>
        /// بررسی واقعی سبدهای خرید رهاشده (ShoppingCartItem که در وضعیت "سبد خرید" باقی مانده و
        /// از آخرین ویرایش آن بیش از آستانه تعیین‌شده گذشته) و فراخوانی callback ارسال پیامک به‌ازای هرکدام.
        /// </summary>
        public async Task<int> TriggerAbandonedCartSmsRemindersAsync(
            int storeId,
            Func<Customer, ShoppingCartItem, Task<bool>> sendSmsCallback,
            int abandonMinutesThreshold = 60)
        {
            var threshold = DateTime.UtcNow.AddMinutes(-abandonMinutesThreshold);

            var abandonedItems = await _shoppingCartRepository.GetAllAsync(query =>
                query.Where(c => c.StoreId == storeId
                    && c.ShoppingCartTypeId == (int)ShoppingCartType.ShoppingCart
                    && c.UpdatedOnUtc < threshold));

            var distinctCustomerIds = abandonedItems.Select(c => c.CustomerId).Distinct().ToList();
            var sentCount = 0;

            foreach (var customerId in distinctCustomerIds)
            {
                var firstItem = abandonedItems.First(c => c.CustomerId == customerId);

                // قبلاً همیشه null پاس داده می‌شد (کامنت خودِ کد قبلی این را اعتراف کرده بود) —
                // یعنی هر callback ارسال پیامکی که به شمارهٔ واقعی مشتری نیاز داشت همیشه شکست می‌خورد.
                var customer = await _customerService.GetCustomerByIdAsync(customerId);
                if (customer == null) continue;

                var wasSent = sendSmsCallback != null && await sendSmsCallback(customer, firstItem);
                if (wasSent) sentCount++;
            }

            return sentCount;
        }

        private static int RandomNumberGenerator() =>
            System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, int.MaxValue);
    }

    public class SpinWheelRewardResult
    {
        public bool IsSuccess { get; set; }
        public string RewardTitle { get; set; }
        public string DiscountCode { get; set; }
        public string Message { get; set; }
    }
}
