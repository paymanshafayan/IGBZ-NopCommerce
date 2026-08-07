namespace Nop.Plugin.Misc.MultiTenantStores.Services
{
    using System;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using Nop.Data;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;

    /// <summary>
    /// یکپارچه‌سازی درگاه‌های شتاب از طریق الگوی Parbad (چند PSP پشت یک اینترفیس واحد).
    /// نکته مهم: این پیاده‌سازی هیچ نتیجه‌ای را به‌صورت ضمنی «موفق» فرض نمی‌کند — تایید نهایی
    /// همیشه از طریق فراخوانی واقعی Endpoint تایید تراکنش PSP انجام می‌شود و نتیجه در
    /// <see cref="PaymentTransactionLedger"/> برای جلوگیری از تایید مضاعف (Replay) ثبت می‌شود.
    /// </summary>
    public interface IParbadPaymentService
    {
        Task<ParbadPaymentRequestResult> RequestPaymentAsync(int storeId, int orderId, decimal amountToman, string gatewayName, string callbackUrl);
        Task<ParbadVerifyResult> VerifyPaymentAsync(int storeId, string trackingNumber, decimal amountToman);
    }

    public class ParbadPaymentService : IParbadPaymentService
    {
        private const string ProviderKeyPrefix = "parbad.";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IRepository<TenantIntegrationCredential> _credentialRepository;
        private readonly IRepository<PaymentTransactionLedger> _ledgerRepository;

        public ParbadPaymentService(
            IHttpClientFactory httpClientFactory,
            IRepository<TenantIntegrationCredential> credentialRepository,
            IRepository<PaymentTransactionLedger> ledgerRepository)
        {
            _httpClientFactory = httpClientFactory;
            _credentialRepository = credentialRepository;
            _ledgerRepository = ledgerRepository;
        }

        public async Task<ParbadPaymentRequestResult> RequestPaymentAsync(int storeId, int orderId, decimal amountToman, string gatewayName, string callbackUrl)
        {
            var credential = await GetActiveCredentialAsync(storeId, gatewayName);
            if (credential == null)
            {
                return new ParbadPaymentRequestResult
                {
                    IsSuccess = false,
                    GatewayName = gatewayName,
                    Message = $"کلید API درگاه «{gatewayName}» برای این فروشگاه فعال یا تایید نشده است."
                };
            }

            var invoiceNumber = $"INV-{storeId}-{orderId}-{Guid.NewGuid():N}".Substring(0, 32);

            var httpClient = _httpClientFactory.CreateClient("ParbadGateway");
            var requestEndpoint = string.IsNullOrWhiteSpace(credential.EndpointOverrideUrl)
                ? $"https://api.parbad.local/v1/gateways/{gatewayName}/request"
                : credential.EndpointOverrideUrl;

            HttpResponseMessage response;
            try
            {
                response = await httpClient.PostAsJsonAsync(requestEndpoint, new ParbadGatewayRequestPayload
                {
                    MerchantApiKey = credential.ApiKey,
                    InvoiceNumber = invoiceNumber,
                    AmountRials = amountToman * 10,
                    CallbackUrl = callbackUrl
                });
            }
            catch (HttpRequestException ex)
            {
                await LogLedgerAsync(storeId, orderId, gatewayName, invoiceNumber, amountToman,
                    PaymentTransactionState.Requested, null, $"HttpRequestException: {ex.Message}");

                return new ParbadPaymentRequestResult
                {
                    IsSuccess = false,
                    GatewayName = gatewayName,
                    Message = "ارتباط با درگاه پرداخت برقرار نشد. لطفاً دوباره تلاش کنید."
                };
            }

            var payload = response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<ParbadGatewayRequestResponse>()
                : null;

            var state = response.IsSuccessStatusCode && payload?.Success == true
                ? PaymentTransactionState.RedirectedToBank
                : PaymentTransactionState.Requested;

            await LogLedgerAsync(storeId, orderId, gatewayName, invoiceNumber, amountToman, state,
                null, await response.Content.ReadAsStringAsync());

            if (state != PaymentTransactionState.RedirectedToBank)
            {
                return new ParbadPaymentRequestResult
                {
                    IsSuccess = false,
                    GatewayName = gatewayName,
                    TrackingNumber = invoiceNumber,
                    Message = payload?.ErrorMessage ?? "درگاه پرداخت درخواست را رد کرد."
                };
            }

            return new ParbadPaymentRequestResult
            {
                IsSuccess = true,
                TrackingNumber = invoiceNumber,
                RedirectUrl = payload.RedirectUrl,
                GatewayName = gatewayName,
                Message = "درخواست پرداخت با موفقیت ثبت شد و کاربر به درگاه بانک منتقل می‌شود."
            };
        }

        public async Task<ParbadVerifyResult> VerifyPaymentAsync(int storeId, string trackingNumber, decimal amountToman)
        {
            var existingLedger = (await _ledgerRepository.GetAllAsync(q =>
                q.Where(l => l.StoreId == storeId && l.TrackingNumber == trackingNumber)))
                .FirstOrDefault();

            // جلوگیری از تایید مضاعف: اگر قبلاً با موفقیت تایید شده، دوباره اعتبار واریز نشود
            if (existingLedger?.State == PaymentTransactionState.VerifiedSuccess)
            {
                return new ParbadVerifyResult
                {
                    IsSuccess = true,
                    AlreadyVerifiedBefore = true,
                    RefId = existingLedger.BankRefId,
                    Message = "این تراکنش پیش‌تر با موفقیت تایید و ثبت شده است (از تایید مضاعف جلوگیری شد)."
                };
            }

            if (existingLedger == null)
            {
                return new ParbadVerifyResult
                {
                    IsSuccess = false,
                    Message = "رکورد درخواست پرداخت متناظر با این کد پیگیری یافت نشد."
                };
            }

            var credential = await GetActiveCredentialAsync(storeId, existingLedger.GatewayName);
            if (credential == null)
            {
                return new ParbadVerifyResult { IsSuccess = false, Message = "کلید API درگاه یافت نشد." };
            }

            var httpClient = _httpClientFactory.CreateClient("ParbadGateway");
            var verifyEndpoint = string.IsNullOrWhiteSpace(credential.EndpointOverrideUrl)
                ? $"https://api.parbad.local/v1/gateways/{existingLedger.GatewayName}/verify"
                : credential.EndpointOverrideUrl.TrimEnd('/') + "/verify";

            ParbadGatewayVerifyResponse verifyPayload;
            string rawBody;
            try
            {
                var response = await httpClient.PostAsJsonAsync(verifyEndpoint, new ParbadGatewayVerifyPayload
                {
                    MerchantApiKey = credential.ApiKey,
                    InvoiceNumber = trackingNumber,
                    AmountRials = amountToman * 10
                });

                rawBody = await response.Content.ReadAsStringAsync();
                verifyPayload = response.IsSuccessStatusCode
                    ? await response.Content.ReadFromJsonAsync<ParbadGatewayVerifyResponse>()
                    : null;
            }
            catch (HttpRequestException ex)
            {
                rawBody = $"HttpRequestException: {ex.Message}";
                verifyPayload = null;
            }

            // مقایسه دقیق مبلغ اعلامی بانک با مبلغ سفارش — جلوگیری از دستکاری مبلغ در سمت کلاینت
            var bankConfirmedAmountMatches = verifyPayload != null
                && verifyPayload.Success
                && verifyPayload.AmountRials == amountToman * 10;

            existingLedger.State = bankConfirmedAmountMatches
                ? PaymentTransactionState.VerifiedSuccess
                : PaymentTransactionState.VerifiedFailed;
            existingLedger.BankRefId = verifyPayload?.RefId;
            existingLedger.RawGatewayResponse = rawBody;
            existingLedger.VerifiedOnUtc = DateTime.UtcNow;
            await _ledgerRepository.UpdateAsync(existingLedger);

            if (!bankConfirmedAmountMatches)
            {
                return new ParbadVerifyResult
                {
                    IsSuccess = false,
                    Message = verifyPayload == null
                        ? "پاسخ نامعتبر یا ناموفق از درگاه بانکی دریافت شد."
                        : "مبلغ تایید شده توسط بانک با مبلغ سفارش مطابقت ندارد."
                };
            }

            return new ParbadVerifyResult
            {
                IsSuccess = true,
                RefId = verifyPayload.RefId,
                Message = "پرداخت با موفقیت توسط بانک تایید شد."
            };
        }

        private async Task<TenantIntegrationCredential> GetActiveCredentialAsync(int storeId, string gatewayName)
        {
            var providerKey = $"{ProviderKeyPrefix}{gatewayName}".ToLowerInvariant();
            var all = await _credentialRepository.GetAllAsync(q =>
                q.Where(c => c.StoreId == storeId && c.ProviderKey == providerKey && c.IsActive && c.IsVerified));
            return all.FirstOrDefault();
        }

        private async Task LogLedgerAsync(int storeId, int orderId, string gatewayName, string trackingNumber,
            decimal amountToman, PaymentTransactionState state, string bankRefId, string rawResponse)
        {
            await _ledgerRepository.InsertAsync(new PaymentTransactionLedger
            {
                StoreId = storeId,
                OrderId = orderId,
                GatewayName = gatewayName,
                TrackingNumber = trackingNumber,
                AmountToman = amountToman,
                State = state,
                BankRefId = bankRefId,
                RawGatewayResponse = rawResponse,
                RequestedOnUtc = DateTime.UtcNow
            });
        }
    }

    public class ParbadPaymentRequestResult
    {
        public bool IsSuccess { get; set; }
        public string TrackingNumber { get; set; }
        public string RedirectUrl { get; set; }
        public string GatewayName { get; set; }
        public string Message { get; set; }
    }

    public class ParbadVerifyResult
    {
        public bool IsSuccess { get; set; }
        public bool AlreadyVerifiedBefore { get; set; }
        public string RefId { get; set; }
        public string Message { get; set; }
    }

    internal class ParbadGatewayRequestPayload
    {
        [JsonPropertyName("merchantApiKey")] public string MerchantApiKey { get; set; }
        [JsonPropertyName("invoiceNumber")] public string InvoiceNumber { get; set; }
        [JsonPropertyName("amountRials")] public decimal AmountRials { get; set; }
        [JsonPropertyName("callbackUrl")] public string CallbackUrl { get; set; }
    }

    internal class ParbadGatewayRequestResponse
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("redirectUrl")] public string RedirectUrl { get; set; }
        [JsonPropertyName("errorMessage")] public string ErrorMessage { get; set; }
    }

    internal class ParbadGatewayVerifyPayload
    {
        [JsonPropertyName("merchantApiKey")] public string MerchantApiKey { get; set; }
        [JsonPropertyName("invoiceNumber")] public string InvoiceNumber { get; set; }
        [JsonPropertyName("amountRials")] public decimal AmountRials { get; set; }
    }

    internal class ParbadGatewayVerifyResponse
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("refId")] public string RefId { get; set; }
        [JsonPropertyName("amountRials")] public decimal AmountRials { get; set; }
    }
}
