namespace Nop.Plugin.Misc.InstagramAssistant.Consumers
{
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Nop.Plugin.Misc.InstagramAssistant.Services;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;
    using Nop.Plugin.Misc.MultiTenantStores.Services;
    using Nop.Services.Customers;

    /// <summary>
    /// پردازشگر حمایت‌های مالی اینستاگرام: کسر از کیف پول واحد حامی (مشتری اپلیکیشن) و واریز واقعی
    /// به کیف پول واحد صاحب فروشگاه. از IWalletService یکپارچهٔ پلتفرم استفاده می‌کند (قبلاً از
    /// یک دفترکل جداگانهٔ مخصوص همین پلاگین استفاده می‌شد).
    /// </summary>
    public class InstagramWalletDonationConsumer
    {
        private readonly ICustomerService _customerService;
        private readonly IInstagramCustomerLinkService _customerLinkService;
        private readonly IWalletService _walletService;

        public InstagramWalletDonationConsumer(
            ICustomerService customerService,
            IInstagramCustomerLinkService customerLinkService,
            IWalletService walletService)
        {
            _customerService = customerService;
            _customerLinkService = customerLinkService;
            _walletService = walletService;
        }

        /// <returns>مبلغ واریزشده به تومان در صورت موفقیت؛ صفر اگر کامنت الگوی حمایت مالی نبود یا حامی معتبر نبود.</returns>
        /// <param name="commentId">شناسهٔ یکتای کامنت اینستاگرام — برای Idempotency واقعی لازم است (وگرنه دو حمایت مالی جداگانه از یک کاربر با هم قاطی می‌شوند).</param>
        public async Task<decimal> ProcessCommentForDonationAsync(int storeId, int storeOwnerCustomerId, string donorInstagramScopedId, string donorUsername, string commentText, string commentId)
        {
            if (string.IsNullOrEmpty(commentText))
                return 0m;

            var match = Regex.Match(commentText.Trim(), @"^\$(\d+)$");
            if (!match.Success)
                return 0m;

            // اولویت با پیوند واقعی IGSID -> Customer است؛ نام‌کاربری فقط Fallback است (ممکن است تغییر کند)
            var donorCustomer = await _customerLinkService.GetCustomerByInstagramScopedIdAsync(donorInstagramScopedId)
                ?? await _customerService.GetCustomerByUsernameAsync(donorUsername);

            if (donorCustomer == null || await _customerService.IsGuestAsync(donorCustomer))
                return 0m;

            var amountInThousands = decimal.Parse(match.Groups[1].Value);
            var totalTomanAmount = amountInThousands * 1000;
            var referenceCode = $"donation-comment-{commentId}";

            var (debitSuccess, _, _) = await _walletService.TryDebitAsync(
                donorCustomer.Id, storeId, totalTomanAmount, WalletTransactionReason.InstagramDonationReceived, referenceCode);

            if (!debitSuccess)
                return 0m; // موجودی حامی کافی نبود

            await _walletService.CreditAsync(storeOwnerCustomerId, storeId, totalTomanAmount,
                WalletTransactionReason.InstagramDonationReceived, referenceCode);

            return totalTomanAmount;
        }
    }
}
