namespace Nop.Plugin.Misc.MultiTenantStores.Services
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Nop.Data;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;

    public class WalletTopUpRequestResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public string RedirectUrl { get; set; }
        public string TrackingNumber { get; set; }
    }

    public class WalletTopUpVerifyResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public bool AlreadyProcessed { get; set; }
        public decimal NewBalanceToman { get; set; }
    }

    /// <summary>
    /// کیف‌پول واحد پلتفرم — تنها منبع حقیقت موجودی مشتری برای **هر** نوع تراکنش: شارژ نقدی، کش‌بک،
    /// حمایت مالی اینستاگرام، جایزهٔ مسابقه، کمیسیون Affiliate، پرداخت سفارش، و مصرف قابلیت‌های AI.
    /// جایگزین سه سرویس/دفترکل جداگانهٔ قبلی (به یادداشت‌های Domain/WalletLedger.cs مراجعه کنید).
    /// </summary>
    public interface IWalletService
    {
        Task<decimal> GetBalanceAsync(int customerId, int storeId);

        Task<decimal> CreditAsync(int customerId, int storeId, decimal amountToman, WalletTransactionReason reason, string referenceCode);

        /// <summary>
        /// کسر Idempotent: اگر برای همین (CustomerId, StoreId, Reason, ReferenceCode) قبلاً رکوردی
        /// ثبت شده، دوباره کسر نمی‌کند (درخواست تکراری/کلیک دوبل). اگر موجودی کافی نبود، شکست
        /// می‌خورد و **هیچ عملیات پرداختی/مصرفی نباید انجام شود**.
        /// </summary>
        Task<(bool success, decimal newBalance, string errorMessage)> TryDebitAsync(
            int customerId, int storeId, decimal amountToman, WalletTransactionReason reason, string referenceCode);

        /// <summary>درخواست شارژ نقدی کیف‌پول از طریق درگاه پرداخت واقعی (Parbad) — کاربر به بانک هدایت می‌شود.</summary>
        Task<WalletTopUpRequestResult> RequestCashTopUpAsync(int storeId, decimal amountToman, string gatewayName, string callbackUrl);

        /// <summary>
        /// Callback بانک — فقط پس از تایید واقعی VerifyPaymentAsync (نه صرفاً بازگشت کاربر از درگاه)
        /// مبلغ به کیف‌پول واریز می‌شود.
        /// </summary>
        Task<WalletTopUpVerifyResult> VerifyCashTopUpAsync(int customerId, int storeId, string trackingNumber, decimal amountToman);
    }

    public class WalletService : IWalletService
    {
        private readonly IRepository<WalletLedger> _ledgerRepository;
        private readonly IParbadPaymentService _paymentService;

        public WalletService(IRepository<WalletLedger> ledgerRepository, IParbadPaymentService paymentService)
        {
            _ledgerRepository = ledgerRepository;
            _paymentService = paymentService;
        }

        public async Task<decimal> GetBalanceAsync(int customerId, int storeId)
        {
            var entries = await _ledgerRepository.GetAllAsync(q =>
                q.Where(e => e.CustomerId == customerId && e.StoreId == storeId));
            return entries.Sum(e => e.AmountToman);
        }

        public async Task<decimal> CreditAsync(int customerId, int storeId, decimal amountToman, WalletTransactionReason reason, string referenceCode)
        {
            if (amountToman <= 0)
                throw new ArgumentException("مبلغ واریزی باید مثبت باشد.", nameof(amountToman));

            await _ledgerRepository.InsertAsync(new WalletLedger
            {
                CustomerId = customerId,
                StoreId = storeId,
                AmountToman = amountToman,
                Reason = reason,
                ReferenceCode = referenceCode,
                CreatedOnUtc = DateTime.UtcNow
            });

            return await GetBalanceAsync(customerId, storeId);
        }

        public async Task<(bool success, decimal newBalance, string errorMessage)> TryDebitAsync(
            int customerId, int storeId, decimal amountToman, WalletTransactionReason reason, string referenceCode)
        {
            if (amountToman <= 0)
                return (true, await GetBalanceAsync(customerId, storeId), null);

            if (!string.IsNullOrEmpty(referenceCode))
            {
                var existingDebit = await _ledgerRepository.GetAllAsync(q =>
                    q.Where(e => e.CustomerId == customerId && e.StoreId == storeId
                        && e.Reason == reason && e.ReferenceCode == referenceCode));

                if (existingDebit.Any())
                    return (true, await GetBalanceAsync(customerId, storeId), null);
            }

            var balance = await GetBalanceAsync(customerId, storeId);
            if (balance < amountToman)
                return (false, balance, "موجودی کیف‌پول کافی نیست.");

            await _ledgerRepository.InsertAsync(new WalletLedger
            {
                CustomerId = customerId,
                StoreId = storeId,
                AmountToman = -amountToman,
                Reason = reason,
                ReferenceCode = referenceCode,
                CreatedOnUtc = DateTime.UtcNow
            });

            var newBalance = await GetBalanceAsync(customerId, storeId);
            return (true, newBalance, null);
        }

        public async Task<WalletTopUpRequestResult> RequestCashTopUpAsync(int storeId, decimal amountToman, string gatewayName, string callbackUrl)
        {
            var result = await _paymentService.RequestPaymentAsync(
                storeId, orderId: 0, amountToman, gatewayName ?? "zarinpal", callbackUrl);

            return new WalletTopUpRequestResult
            {
                IsSuccess = result.IsSuccess,
                Message = result.Message,
                RedirectUrl = result.RedirectUrl,
                TrackingNumber = result.TrackingNumber
            };
        }

        public async Task<WalletTopUpVerifyResult> VerifyCashTopUpAsync(int customerId, int storeId, string trackingNumber, decimal amountToman)
        {
            var verifyResult = await _paymentService.VerifyPaymentAsync(storeId, trackingNumber, amountToman);
            if (!verifyResult.IsSuccess)
                return new WalletTopUpVerifyResult { IsSuccess = false, Message = verifyResult.Message };

            if (verifyResult.AlreadyVerifiedBefore)
                return new WalletTopUpVerifyResult
                {
                    IsSuccess = true,
                    AlreadyProcessed = true,
                    Message = "این تراکنش قبلاً پردازش شده است.",
                    NewBalanceToman = await GetBalanceAsync(customerId, storeId)
                };

            var newBalance = await CreditAsync(customerId, storeId, amountToman, WalletTransactionReason.CashTopUp, trackingNumber);
            return new WalletTopUpVerifyResult { IsSuccess = true, NewBalanceToman = newBalance };
        }
    }
}
