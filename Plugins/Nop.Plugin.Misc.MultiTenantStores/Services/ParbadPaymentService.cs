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

        // ── آدرس‌های واقعی pay.ir (مستندات رسمی: https://docs.pay.ir/gateway) ──
        private const string PayIrSendEndpoint = "https://pay.ir/pg/send";
        private const string PayIrVerifyEndpoint = "https://pay.ir/pg/verify";
        private const string PayIrGatewayPage = "https://pay.ir/pg";

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

        private static bool IsPayIr(string gatewayName) =>
            string.Equals(gatewayName, "payir", StringComparison.OrdinalIgnoreCase);

        public async Task<ParbadPaymentRequestResult> RequestPaymentAsync(int storeId, int orderId, decimal amountToman, string gatewayName, string callbackUrl)
        {
            // مسیر واقعی pay.ir (مستندات رسمی) — در غیر این صورت الگوی Parbad چندبانکه
            if (IsPayIr(gatewayName))
                return await RequestPayIrAsync(storeId, orderId, amountToman, gatewayName, callbackUrl);

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

            // مسیر واقعی pay.ir — تایید با token (که در URL بازگشت از درگاه آمده)
            if (existingLedger != null && IsPayIr(existingLedger.GatewayName))
                return await VerifyPayIrAsync(storeId, trackingNumber, amountToman, existingLedger);

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

        // ────────────────────────── pay.ir (رسمی) ──────────────────────────

        /// <summary>
        /// درخواست پرداخت از pay.ir طبق مستندات رسمی:
        /// POST https://pay.ir/pg/send ← { api, amount(ریال), redirect, factorNumber, description }
        /// پاسخ موفق: { status: 1, token } — سپس کاربر به https://pay.ir/pg/{token} هدایت می‌شود.
        /// توکن در ledger به‌عنوان TrackingNumber ثبت می‌شود چون در URL بازگشت از درگاه می‌آید.
        /// </summary>
        private async Task<ParbadPaymentRequestResult> RequestPayIrAsync(int storeId, int orderId, decimal amountToman, string gatewayName, string callbackUrl)
        {
            var credential = await GetActiveCredentialAsync(storeId, gatewayName);
            if (credential == null)
            {
                return new ParbadPaymentRequestResult
                {
                    IsSuccess = false,
                    GatewayName = gatewayName,
                    Message = "کلید API درگاه pay.ir برای این فروشگاه فعال یا تایید نشده است."
                };
            }

            var factorNumber = $"INV-{storeId}-{orderId}-{Guid.NewGuid():N}".Substring(0, 32);
            var httpClient = _httpClientFactory.CreateClient("ParbadGateway");

            string rawBody;
            PayIrSendResponse payload;
            try
            {
                var response = await httpClient.PostAsJsonAsync(PayIrSendEndpoint, new PayIrSendPayload
                {
                    Api = credential.ApiKey,
                    Amount = (long)(amountToman * 10),
                    Redirect = callbackUrl,
                    FactorNumber = factorNumber,
                    Description = $"سفارش {orderId}"
                });

                rawBody = await response.Content.ReadAsStringAsync();
                payload = response.IsSuccessStatusCode
                    ? await response.Content.ReadFromJsonAsync<PayIrSendResponse>()
                    : null;
            }
            catch (HttpRequestException ex)
            {
                rawBody = $"HttpRequestException: {ex.Message}";
                payload = null;
            }

            if (payload != null && payload.Status == 1 && !string.IsNullOrWhiteSpace(payload.Token))
            {
                // ثبت ledger با TrackingNumber = token (چون callback کاربر token را برمی‌گرداند)
                await LogLedgerAsync(storeId, orderId, gatewayName, payload.Token, amountToman,
                    PaymentTransactionState.RedirectedToBank, null, rawBody);

                return new ParbadPaymentRequestResult
                {
                    IsSuccess = true,
                    TrackingNumber = payload.Token,
                    RedirectUrl = $"{PayIrGatewayPage}/{payload.Token}",
                    GatewayName = gatewayName,
                    Message = "درخواست پرداخت با موفقیت ثبت شد و کاربر به درگاه pay.ir منتقل می‌شود."
                };
            }

            await LogLedgerAsync(storeId, orderId, gatewayName, factorNumber, amountToman,
                PaymentTransactionState.Requested, null, rawBody);

            return new ParbadPaymentRequestResult
            {
                IsSuccess = false,
                GatewayName = gatewayName,
                TrackingNumber = factorNumber,
                Message = payload?.ErrorMessage ?? payload?.Message ?? "درگاه pay.ir درخواست را رد کرد."
            };
        }

        /// <summary>
        /// تایید تراکنش pay.ir طبق مستندات رسمی:
        /// POST https://pay.ir/pg/verify ← { api, token } → { status, amount, transId, ... }
        /// موفقیت فقط وقتی است که status==1 و مبلغ تاییدشده دقیقاً برابر مبلغ سفارش (به ریال) باشد.
        /// </summary>
        private async Task<ParbadVerifyResult> VerifyPayIrAsync(int storeId, string trackingNumber, decimal amountToman, PaymentTransactionLedger existingLedger)
        {
            // جلوگیری از تایید مضاعف (Replay)
            if (existingLedger.State == PaymentTransactionState.VerifiedSuccess)
            {
                return new ParbadVerifyResult
                {
                    IsSuccess = true,
                    AlreadyVerifiedBefore = true,
                    RefId = existingLedger.BankRefId,
                    Message = "این تراکنش پیش‌تر با موفقیت تایید و ثبت شده است (از تایید مضاعف جلوگیری شد)."
                };
            }

            var credential = await GetActiveCredentialAsync(storeId, existingLedger.GatewayName);
            if (credential == null)
            {
                return new ParbadVerifyResult { IsSuccess = false, Message = "کلید API درگاه pay.ir یافت نشد." };
            }

            var httpClient = _httpClientFactory.CreateClient("ParbadGateway");

            PayIrVerifyResponse verifyPayload;
            string rawBody;
            try
            {
                var response = await httpClient.PostAsJsonAsync(PayIrVerifyEndpoint, new PayIrVerifyPayload
                {
                    Api = credential.ApiKey,
                    Token = trackingNumber
                });

                rawBody = await response.Content.ReadAsStringAsync();
                verifyPayload = response.IsSuccessStatusCode
                    ? await response.Content.ReadFromJsonAsync<PayIrVerifyResponse>()
                    : null;
            }
            catch (HttpRequestException ex)
            {
                rawBody = $"HttpRequestException: {ex.Message}";
                verifyPayload = null;
            }

            // مقایسهٔ دقیق مبلغ اعلامی pay.ir با مبلغ سفارش (جلوگیری از دستکاری مبلغ)
            var verified = verifyPayload != null
                && verifyPayload.Status == 1
                && verifyPayload.Amount == (long)(amountToman * 10);

            existingLedger.State = verified
                ? PaymentTransactionState.VerifiedSuccess
                : PaymentTransactionState.VerifiedFailed;
            existingLedger.BankRefId = verifyPayload?.TransId;
            existingLedger.RawGatewayResponse = rawBody;
            existingLedger.VerifiedOnUtc = DateTime.UtcNow;
            await _ledgerRepository.UpdateAsync(existingLedger);

            if (!verified)
            {
                return new ParbadVerifyResult
                {
                    IsSuccess = false,
                    Message = verifyPayload == null
                        ? "پاسخ نامعتبر یا ناموفق از درگاه pay.ir دریافت شد."
                        : "مبلغ تایید شده توسط pay.ir با مبلغ سفارش مطابقت ندارد."
                };
            }

            return new ParbadVerifyResult
            {
                IsSuccess = true,
                RefId = verifyPayload.TransId,
                Message = "پرداخت با موفقیت توسط pay.ir تایید شد."
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

    // ── DTOهای pay.ir (طبق مستندات رسمی docs.pay.ir/gateway) ──

    internal class PayIrSendPayload
    {
        [JsonPropertyName("api")] public string Api { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; }
        [JsonPropertyName("redirect")] public string Redirect { get; set; }
        [JsonPropertyName("mobile")] public string Mobile { get; set; }
        [JsonPropertyName("factorNumber")] public string FactorNumber { get; set; }
        [JsonPropertyName("description")] public string Description { get; set; }
    }

    internal class PayIrSendResponse
    {
        [JsonPropertyName("status")] public int Status { get; set; }
        [JsonPropertyName("token")] public string Token { get; set; }
        [JsonPropertyName("errorMessage")] public string ErrorMessage { get; set; }
        [JsonPropertyName("message")] public string Message { get; set; }
    }

    internal class PayIrVerifyPayload
    {
        [JsonPropertyName("api")] public string Api { get; set; }
        [JsonPropertyName("token")] public string Token { get; set; }
    }

    internal class PayIrVerifyResponse
    {
        [JsonPropertyName("status")] public int Status { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; }
        [JsonPropertyName("transId")] public string TransId { get; set; }
        [JsonPropertyName("factorNumber")] public string FactorNumber { get; set; }
        [JsonPropertyName("cardNumber")] public string CardNumber { get; set; }
        [JsonPropertyName("message")] public string Message { get; set; }
    }
}
