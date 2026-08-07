namespace Nop.Plugin.Misc.MultiTenantStores.Services
{
    using System;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Threading.Tasks;
    using Nop.Core.Domain.Customers;
    using Nop.Data;
    using Nop.Services.Common;
    using Nop.Services.Customers;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;

    public interface IAffiliateMarketingService
    {
        Task<string> GetOrCreateReferralCodeAsync(int customerId, int storeId);
        Task<Customer> GetCustomerByReferralCodeAsync(string code);
        Task CaptureReferralOnRegistrationAsync(int newCustomerId, string referralCode);
        Task<AffiliateCommissionLedger> ProcessOrderCommissionAsync(int storeId, int orderId, int referredCustomerId, decimal orderTotalToman, decimal commissionPercent = 5);
        Task<AffiliateStatsDto> GetReferralStatsAsync(int customerId, int storeId);
        Task<AffiliateWithdrawalRequest> RequestWithdrawalAsync(int customerId, int storeId, decimal amountToman, string bankAccountInfo);
        Task<System.Collections.Generic.IList<AffiliateWithdrawalRequest>> GetPendingWithdrawalRequestsAsync(int storeId);
        Task<bool> ApproveWithdrawalAsync(int requestId, string adminNote);
        Task RejectWithdrawalAsync(int requestId, string adminNote);
    }

    public class AffiliateMarketingService : IAffiliateMarketingService
    {
        private const string ReferrerAttributeKey = "AffiliateReferrerCustomerId";
        private static readonly char[] CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray(); // بدون کاراکترهای مشابه (0/O، 1/I)

        private readonly IRepository<AffiliateReferralCode> _codeRepository;
        private readonly IRepository<AffiliateCommissionLedger> _commissionRepository;
        private readonly IRepository<AffiliateWithdrawalRequest> _withdrawalRepository;
        private readonly ICustomerService _customerService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IWalletService _walletService;

        public AffiliateMarketingService(
            IRepository<AffiliateReferralCode> codeRepository,
            IRepository<AffiliateCommissionLedger> commissionRepository,
            IRepository<AffiliateWithdrawalRequest> withdrawalRepository,
            ICustomerService customerService,
            IGenericAttributeService genericAttributeService,
            IWalletService walletService)
        {
            _codeRepository = codeRepository;
            _commissionRepository = commissionRepository;
            _withdrawalRepository = withdrawalRepository;
            _customerService = customerService;
            _genericAttributeService = genericAttributeService;
            _walletService = walletService;
        }

        /// <summary>
        /// کد معرف کوتاه واقعی (نه لینک طولانی پیش‌فرض ناپ‌کامرس). یک‌بار تولید می‌شود و ثابت می‌ماند.
        /// </summary>
        public async Task<string> GetOrCreateReferralCodeAsync(int customerId, int storeId)
        {
            var existing = (await _codeRepository.GetAllAsync(q =>
                q.Where(c => c.CustomerId == customerId && c.StoreId == storeId))).FirstOrDefault();

            if (existing != null)
                return existing.Code;

            string code;
            do
            {
                code = GenerateRandomCode();
            }
            while ((await _codeRepository.GetAllAsync(q => q.Where(c => c.Code == code))).Any());

            await _codeRepository.InsertAsync(new AffiliateReferralCode
            {
                CustomerId = customerId,
                StoreId = storeId,
                Code = code,
                CreatedOnUtc = DateTime.UtcNow
            });

            return code;
        }

        public async Task<Customer> GetCustomerByReferralCodeAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return null;

            var record = (await _codeRepository.GetAllAsync(q =>
                q.Where(c => c.Code == code.Trim().ToUpperInvariant()))).FirstOrDefault();

            return record == null ? null : await _customerService.GetCustomerByIdAsync(record.CustomerId);
        }

        /// <summary>
        /// در لحظهٔ ثبت‌نام مشتری جدید، اگر کد معرف معتبر بود، شناسهٔ معرف روی مشتری جدید
        /// به‌صورت Generic Attribute ذخیره می‌شود (طبق پیشنهاد معماری خودِ راهنما).
        /// </summary>
        public async Task CaptureReferralOnRegistrationAsync(int newCustomerId, string referralCode)
        {
            var referrer = await GetCustomerByReferralCodeAsync(referralCode);
            if (referrer == null || referrer.Id == newCustomerId)
                return; // کد نامعتبر یا معرفی خود شخص به خودش

            var newCustomer = await _customerService.GetCustomerByIdAsync(newCustomerId);
            if (newCustomer == null)
                return;

            await _genericAttributeService.SaveAttributeAsync(newCustomer, ReferrerAttributeKey, referrer.Id);
        }

        /// <summary>
        /// ثبت واقعی کمیسیون در دفترکل — به‌جای فقط «محاسبه و ادعای واریز» در نسخهٔ قبلی.
        /// </summary>
        public async Task<AffiliateCommissionLedger> ProcessOrderCommissionAsync(
            int storeId, int orderId, int referredCustomerId, decimal orderTotalToman, decimal commissionPercent = 5)
        {
            var referredCustomer = await _customerService.GetCustomerByIdAsync(referredCustomerId);
            if (referredCustomer == null)
                return null;

            var referrerId = await _genericAttributeService.GetAttributeAsync<int>(referredCustomer, ReferrerAttributeKey);
            if (referrerId <= 0)
                return null; // این مشتری از طریق هیچ معرفی وارد نشده

            // جلوگیری از کمیسیون تکراری روی همان سفارش
            var alreadyProcessed = (await _commissionRepository.GetAllAsync(q =>
                q.Where(c => c.OrderId == orderId && c.ReferrerCustomerId == referrerId))).Any();
            if (alreadyProcessed)
                return null;

            var commissionAmount = Math.Round((orderTotalToman * commissionPercent) / 100, 0);

            var ledgerEntry = new AffiliateCommissionLedger
            {
                ReferrerCustomerId = referrerId,
                ReferredCustomerId = referredCustomerId,
                StoreId = storeId,
                OrderId = orderId,
                CommissionToman = commissionAmount,
                State = AffiliateCommissionState.Earned,
                CreatedOnUtc = DateTime.UtcNow
            };

            await _commissionRepository.InsertAsync(ledgerEntry);

            // کمیسیون بلافاصله در کیف‌پول واحد قابل‌خرج می‌شود (نه فقط یک عدد در گزارش).
            await _walletService.CreditAsync(
                referrerId, storeId, commissionAmount, WalletTransactionReason.AffiliateCommissionEarned, $"affiliate-commission-order-{orderId}");

            return ledgerEntry;
        }

        public async Task<AffiliateStatsDto> GetReferralStatsAsync(int customerId, int storeId)
        {
            var commissions = await _commissionRepository.GetAllAsync(q =>
                q.Where(c => c.ReferrerCustomerId == customerId && c.StoreId == storeId));

            var referralCode = (await _codeRepository.GetAllAsync(q =>
                q.Where(c => c.CustomerId == customerId && c.StoreId == storeId))).FirstOrDefault();

            var totalEarned = commissions.Sum(c => c.CommissionToman);

            // موجودی قابل‌برداشت/قابل‌خرج از کیف‌پول واحد خوانده می‌شود (چون کمیسیون مستقیماً به آن
            // واریز می‌شود و برداشت‌های تاییدشده مستقیماً از آن کسر می‌شوند)، نه با تفریق دستی.
            var walletBalance = await _walletService.GetBalanceAsync(customerId, storeId);

            return new AffiliateStatsDto
            {
                ReferralCode = referralCode?.Code,
                TotalReferredCustomers = commissions.Select(c => c.ReferredCustomerId).Distinct().Count(),
                TotalEarnedToman = totalEarned,
                AvailableBalanceToman = walletBalance
            };
        }

        public async Task<AffiliateWithdrawalRequest> RequestWithdrawalAsync(int customerId, int storeId, decimal amountToman, string bankAccountInfo)
        {
            var walletBalance = await _walletService.GetBalanceAsync(customerId, storeId);
            if (amountToman <= 0 || amountToman > walletBalance)
                throw new InvalidOperationException("مبلغ درخواستی بیشتر از موجودی کیف‌پول است.");

            var request = new AffiliateWithdrawalRequest
            {
                CustomerId = customerId,
                StoreId = storeId,
                AmountToman = amountToman,
                BankAccountInfo = bankAccountInfo,
                Status = AffiliateWithdrawalStatus.Requested,
                RequestedOnUtc = DateTime.UtcNow
            };

            await _withdrawalRepository.InsertAsync(request);
            return request;
        }

        public async Task<System.Collections.Generic.IList<AffiliateWithdrawalRequest>> GetPendingWithdrawalRequestsAsync(int storeId)
        {
            return await _withdrawalRepository.GetAllAsync(q =>
                q.Where(w => w.StoreId == storeId && w.Status == AffiliateWithdrawalStatus.Requested)
                 .OrderBy(w => w.RequestedOnUtc));
        }

        /// <summary>
        /// تایید برداشت = خروج واقعی پول از کیف‌پول واحد (چون قرار است به‌صورت بانکی به کاربر
        /// واریز شود، دیگر نباید در کیف‌پول قابل‌خرج بماند). اگر موجودی از زمان درخواست تغییر کرده و
        /// دیگر کافی نبود، false برمی‌گردد و وضعیت درخواست همچنان «در انتظار» می‌ماند.
        /// </summary>
        public async Task<bool> ApproveWithdrawalAsync(int requestId, string adminNote)
        {
            var request = await _withdrawalRepository.GetByIdAsync(requestId);
            if (request == null) return false;

            var (debitSuccess, _, _) = await _walletService.TryDebitAsync(
                request.CustomerId, request.StoreId, request.AmountToman,
                WalletTransactionReason.AffiliateWithdrawalToBank, $"affiliate-withdrawal-{request.Id}");

            if (!debitSuccess)
                return false;

            request.Status = AffiliateWithdrawalStatus.Approved;
            request.AdminNote = adminNote;
            request.ProcessedOnUtc = DateTime.UtcNow;
            await _withdrawalRepository.UpdateAsync(request);
            return true;
        }

        public async Task RejectWithdrawalAsync(int requestId, string adminNote)
        {
            var request = await _withdrawalRepository.GetByIdAsync(requestId);
            if (request == null) return;

            request.Status = AffiliateWithdrawalStatus.Rejected;
            request.AdminNote = adminNote;
            request.ProcessedOnUtc = DateTime.UtcNow;
            await _withdrawalRepository.UpdateAsync(request);
        }

        private static string GenerateRandomCode()
        {
            var buffer = new char[6];
            for (var i = 0; i < buffer.Length; i++)
                buffer[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(0, CodeAlphabet.Length)];
            return new string(buffer);
        }
    }

    public class AffiliateStatsDto
    {
        public string ReferralCode { get; set; }
        public int TotalReferredCustomers { get; set; }
        public decimal TotalEarnedToman { get; set; }
        public decimal AvailableBalanceToman { get; set; }
    }
}
