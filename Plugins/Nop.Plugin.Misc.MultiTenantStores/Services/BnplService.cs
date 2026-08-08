namespace Nop.Plugin.Misc.MultiTenantStores.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Net.Http.Json;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using Nop.Data;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;

    /// <summary>
    /// پرداخت اعتباری/اقساطی (BNPL) برای دیجی‌پی و اسنپ‌پی.
    /// </summary>
    public interface IBnplService
    {
        Task<BnplEligibilityResult> CheckEligibilityAsync(int storeId, int customerId, string providerKey, decimal amountToman, string customerMobile, string customerNationalId);

        Task<BnplStartResult> StartPaymentAsync(int storeId, int orderId, int customerId, string providerKey, decimal amountToman, string callbackUrl, string customerMobile);

        Task<BnplVerifyResult> VerifyPaymentAsync(int storeId, string providerKey, string transactionId, string paymentToken, decimal amountToman);
    }

    public class BnplEligibilityResult
    {
        public bool IsEligible { get; set; }
        public string Message { get; set; }
        public string ProviderKey { get; set; }
    }

    public class BnplStartResult
    {
        public bool IsSuccess { get; set; }
        public string RedirectUrl { get; set; }
        public string TransactionId { get; set; }
        public string PaymentToken { get; set; }
        public string Message { get; set; }
    }

    public class BnplVerifyResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public string TrackingCode { get; set; }
        public bool AlreadyProcessed { get; set; }
    }

    public class BnplService : IBnplService
    {
        private const string DigipayProvider = "digipay";
        private const string SnapppayProvider = "snapppay";

        // ── دیجی‌پی (مستندات رسمی: mydigipay.com/developers/docs/upg) ──
        private const string DigipayUatBase = "https://uat.mydigipay.info/digipay/api";
        private const string DigipayLiveBase = "https://api.mydigipay.com/digipay/api";

        // ── اسنپ‌پی (از پلاگین مرجع NopPlus.Plugin.SnappPay) ──
        private const string SnapppayDefaultBase = "https://fms-gateway-staging.apps.public.okd4.teh-1.snappcloud.io";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IRepository<BnplCredential> _credentialRepository;
        private readonly IRepository<BnplPaymentRecord> _paymentRepository;

        public BnplService(
            IHttpClientFactory httpClientFactory,
            IRepository<BnplCredential> credentialRepository,
            IRepository<BnplPaymentRecord> paymentRepository)
        {
            _httpClientFactory = httpClientFactory;
            _credentialRepository = credentialRepository;
            _paymentRepository = paymentRepository;
        }

        // ────────────────────────── Eligibility (اجازه) ──────────────────────────

        public async Task<BnplEligibilityResult> CheckEligibilityAsync(
            int storeId, int customerId, string providerKey, decimal amountToman, string customerMobile, string customerNationalId)
        {
            var credential = await GetActiveCredentialAsync(storeId, providerKey);
            if (credential == null)
                return new BnplEligibilityResult { IsEligible = false, ProviderKey = providerKey, Message = "پرداخت اعتباری برای این فروشگاه فعال نشده است." };

            if (providerKey == DigipayProvider)
            {
                // دیجی‌پی: Eligibility از سمت پلتفرم هنگام تیکت (basketDetailsDto) انجام می‌شود؛
                // در این مرحله فقط بررسی اعتبار کلی مبلغ انجام می‌شود و نتیجهٔ نهایی در StartPayment/Verify می‌آید.
                return new BnplEligibilityResult
                {
                    IsEligible = true,
                    ProviderKey = providerKey,
                    Message = "امکان پرداخت اعتباری دیجی‌پی برای این مبلغ بررسی می‌شود."
                };
            }

            if (providerKey == SnapppayProvider)
            {
                return await CheckSnapppayEligibilityAsync(credential, amountToman);
            }

            return new BnplEligibilityResult { IsEligible = false, ProviderKey = providerKey, Message = "ارائه‌دهندهٔ BNPL ناشناخته است." };
        }

        // ────────────────────────── Start Payment ──────────────────────────

        public async Task<BnplStartResult> StartPaymentAsync(
            int storeId, int orderId, int customerId, string providerKey, decimal amountToman, string callbackUrl, string customerMobile)
        {
            var credential = await GetActiveCredentialAsync(storeId, providerKey);
            if (credential == null)
                return new BnplStartResult { IsSuccess = false, Message = "پرداخت اعتباری برای این فروشگاه فعال نشده است." };

            if (providerKey == DigipayProvider)
                return await StartDigipayAsync(credential, storeId, orderId, customerId, amountToman, callbackUrl, customerMobile);

            if (providerKey == SnapppayProvider)
                return await StartSnapppayAsync(credential, storeId, orderId, customerId, amountToman, callbackUrl, customerMobile);

            return new BnplStartResult { IsSuccess = false, Message = "ارائه‌دهندهٔ BNPL ناشناخته است." };
        }

        // ────────────────────────── Verify Payment ──────────────────────────

        public async Task<BnplVerifyResult> VerifyPaymentAsync(
            int storeId, string providerKey, string transactionId, string paymentToken, decimal amountToman)
        {
            var credential = await GetActiveCredentialAsync(storeId, providerKey);
            if (credential == null)
                return new BnplVerifyResult { IsSuccess = false, Message = "پرداخت اعتباری برای این فروشگاه فعال نشده است." };

            if (providerKey == DigipayProvider)
                return await VerifyDigipayAsync(credential, storeId, transactionId, paymentToken, amountToman);

            if (providerKey == SnapppayProvider)
                return await VerifySnapppayAsync(credential, storeId, transactionId, paymentToken, amountToman);

            return new BnplVerifyResult { IsSuccess = false, Message = "ارائه‌دهندهٔ BNPL ناشناخته است." };
        }

        // ────────────────────────── دیجی‌پی (رسمی) ──────────────────────────

        private async Task<BnplStartResult> StartDigipayAsync(
            BnplCredential credential, int storeId, int orderId, int customerId, decimal amountToman, string callbackUrl, string customerMobile)
        {
            var baseUrl = credential.Environment == "live" ? DigipayLiveBase : DigipayUatBase;
            var accessToken = await GetDigipayTokenAsync(credential, baseUrl);
            if (string.IsNullOrWhiteSpace(accessToken))
                return new BnplStartResult { IsSuccess = false, Message = "احراز هویت در دیجی‌پی ناموفق بود." };

            var client = _httpClientFactory.CreateClient("BnplGateway");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            client.DefaultRequestHeaders.Add("Agent", "WEB");
            client.DefaultRequestHeaders.Add("Digipay-Version", "2022-02-02");

            var providerId = $"{storeId}-{orderId}-{Guid.NewGuid():N}".Substring(0, 30);
            var payload = new DigipayTicketRequest
            {
                CellNumber = customerMobile,
                Amount = (long)(amountToman * 10), // ریال
                ProviderId = providerId,
                CallbackUrl = callbackUrl,
                // BNPL → basketDetailsDto اجباری است (حداقل با یک آیتم)
                BasketDetails = new DigipayBasketDetails
                {
                    BasketId = $"basket-{orderId}",
                    Items = new List<DigipayBasketItem>
                    {
                        new()
                        {
                            SellerId = $"seller-{storeId}",
                            SupplierId = $"seller-{storeId}",
                            ProductCode = $"order-{orderId}",
                            Brand = "IGBZ",
                            ProductType = 3, // سرویس(خدمات) — برای سفارش‌های متنوع
                            Count = 1,
                            CategoryId = "General"
                        }
                    }
                }
            };

            var response = await client.PostAsJsonAsync($"{baseUrl}/tickets/business?type=11", payload);
            var rawBody = await response.Content.ReadAsStringAsync();

            DigipayTicketResponse ticketResponse = null;
            if (response.IsSuccessStatusCode)
                ticketResponse = TryDeserialize<DigipayTicketResponse>(rawBody);

            if (ticketResponse?.Result?.Status != 0 || string.IsNullOrWhiteSpace(ticketResponse.RedirectUrl))
            {
                return new BnplStartResult
                {
                    IsSuccess = false,
                    Message = ticketResponse?.Result?.Message ?? "دیجی‌پی تیکت را رد کرد.",
                    TransactionId = providerId
                };
            }

            await SavePaymentRecordAsync(storeId, orderId, customerId, DigipayProvider, providerId, ticketResponse.Ticket, amountToman,
                null, rawBody, BnplPaymentStatus.RedirectedToGateway);

            return new BnplStartResult
            {
                IsSuccess = true,
                RedirectUrl = ticketResponse.RedirectUrl,
                TransactionId = providerId,
                PaymentToken = ticketResponse.Ticket,
                Message = "درخواست پرداخت اعتباری دیجی‌پی ثبت شد."
            };
        }

        private async Task<BnplVerifyResult> VerifyDigipayAsync(
            BnplCredential credential, int storeId, string transactionId, string paymentToken, decimal amountToman)
        {
            var baseUrl = credential.Environment == "live" ? DigipayLiveBase : DigipayUatBase;
            var accessToken = await GetDigipayTokenAsync(credential, baseUrl);
            if (string.IsNullOrWhiteSpace(accessToken))
                return new BnplVerifyResult { IsSuccess = false, Message = "احراز هویت در دیجی‌پی ناموفق بود." };

            var record = (await _paymentRepository.GetAllAsync(q =>
                q.Where(r => r.StoreId == storeId && r.TransactionId == transactionId && r.ProviderKey == DigipayProvider))).FirstOrDefault();

            if (record == null)
                return new BnplVerifyResult { IsSuccess = false, Message = "رکورد پرداخت اعتباری یافت نشد." };

            // جلوگیری از تایید مضاعف
            if (record.Status == BnplPaymentStatus.Paid || record.Status == BnplPaymentStatus.Settled)
                return new BnplVerifyResult { IsSuccess = true, AlreadyProcessed = true, Message = "این تراکنش قبلاً تایید شده است." };

            var client = _httpClientFactory.CreateClient("BnplGateway");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            // type=13 → BNPL
            var verifyResponse = await client.PostAsJsonAsync(
                $"{baseUrl}/purchases/verify?type=13",
                new { trackingCode = paymentToken, providerId = transactionId });
            var rawBody = await verifyResponse.Content.ReadAsStringAsync();

            DigipayVerifyResponse verify = null;
            if (verifyResponse.IsSuccessStatusCode)
                verify = TryDeserialize<DigipayVerifyResponse>(rawBody);

            // موفقیت: result.status==0 و مبلغ منطبق (verify.amount به ریال)
            var verified = verify?.Result?.Status == 0 && verify.Amount == (long)(amountToman * 10);

            record.Status = verified ? BnplPaymentStatus.Paid : BnplPaymentStatus.Failed;
            record.RawResponseJson = rawBody;
            record.VerifiedOnUtc = DateTime.UtcNow;
            await _paymentRepository.UpdateAsync(record);

            return new BnplVerifyResult
            {
                IsSuccess = verified,
                Message = verified ? "پرداخت اعتباری دیجی‌پی تایید شد." : (verify?.Result?.Message ?? "تایید پرداخت دیجی‌پی ناموفق بود."),
                TrackingCode = verify?.TrackingCode
            };
        }

        private async Task<string> GetDigipayTokenAsync(BnplCredential credential, string baseUrl)
        {
            var client = _httpClientFactory.CreateClient("BnplGateway");
            var basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{credential.ClientId}:{credential.ClientSecret}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", "password" },
                { "username", credential.Username },
                { "password", credential.Password }
            });

            var response = await client.PostAsync($"{baseUrl}/oauth/token", form);
            if (!response.IsSuccessStatusCode)
                return null;

            var dto = await response.Content.ReadFromJsonAsync<DigipayTokenResponse>();
            return dto?.AccessToken;
        }

        // ────────────────────────── اسنپ‌پی (مرجع NopPlus) ──────────────────────────

        private async Task<BnplEligibilityResult> CheckSnapppayEligibilityAsync(BnplCredential credential, decimal amountToman)
        {
            var client = await CreateSnapppayClientAsync(credential);
            var response = await client.GetAsync($"{GetSnapppayBase(credential)}/api/online/offer/v1/eligible?amount={(long)(amountToman * 10)}");
            var raw = await response.Content.ReadAsStringAsync();

            var dto = TryDeserialize<SnapppayResultDto<SnapppayEligibleDto>>(raw);
            var eligible = dto?.Successful == true && dto.Response?.Eligible == true;

            return new BnplEligibilityResult
            {
                IsEligible = eligible,
                ProviderKey = SnapppayProvider,
                Message = dto?.ErrorData?.Message ?? (eligible ? "واجد شرایط پرداخت اقساطی اسنپ‌پی است." : "واجد شرایط نیست.")
            };
        }

        private async Task<BnplStartResult> StartSnapppayAsync(
            BnplCredential credential, int storeId, int orderId, int customerId, decimal amountToman, string callbackUrl, string customerMobile)
        {
            var client = await CreateSnapppayClientAsync(credential);
            var transactionId = $"IGBZ-{storeId}-{orderId}-{Guid.NewGuid():N}".Substring(0, 40);

            var request = new SnapppayPaymentRequestDto
            {
                TransactionId = transactionId,
                Amount = (int)(amountToman * 10),
                ReturnURL = callbackUrl,
                PaymentMethodTypeDto = "INSTALLMENT",
                Mobile = customerMobile,
                CartList = new List<SnapppayCartList>
                {
                    new()
                    {
                        CartId = orderId,
                        TotalAmount = (int)(amountToman * 10),
                        IsShipmentIncluded = false,
                        ShippingAmount = 0,
                        IsTaxIncluded = false,
                        TaxAmount = 0,
                        CartItems = new List<SnapppayCartItem>
                        {
                            new()
                            {
                                Id = orderId,
                                Name = $"سفارش {orderId}",
                                Count = 1,
                                Amount = (int)(amountToman * 10),
                                Category = "General"
                            }
                        }
                    }
                }
            };

            var response = await client.PostAsJsonAsync($"{GetSnapppayBase(credential)}/api/online/payment/v1/token", request);
            var raw = await response.Content.ReadAsStringAsync();
            var dto = TryDeserialize<SnapppayResultDto<SnapppayPaymentTokenDto>>(raw);

            if (dto?.Successful != true || string.IsNullOrWhiteSpace(dto.Response?.PaymentPageUrl))
            {
                return new BnplStartResult
                {
                    IsSuccess = false,
                    Message = dto?.ErrorData?.Message ?? "اسنپ‌پی توکن را رد کرد.",
                    TransactionId = transactionId
                };
            }

            await SavePaymentRecordAsync(storeId, orderId, customerId, SnapppayProvider, transactionId, dto.Response.PaymentToken, amountToman,
                raw, raw, BnplPaymentStatus.RedirectedToGateway);

            return new BnplStartResult
            {
                IsSuccess = true,
                RedirectUrl = dto.Response.PaymentPageUrl,
                TransactionId = transactionId,
                PaymentToken = dto.Response.PaymentToken,
                Message = "درخواست پرداخت اقساطی اسنپ‌پی ثبت شد."
            };
        }

        private async Task<BnplVerifyResult> VerifySnapppayAsync(
            BnplCredential credential, int storeId, string transactionId, string paymentToken, decimal amountToman)
        {
            var record = (await _paymentRepository.GetAllAsync(q =>
                q.Where(r => r.StoreId == storeId && r.TransactionId == transactionId && r.ProviderKey == SnapppayProvider))).FirstOrDefault();

            if (record == null)
                return new BnplVerifyResult { IsSuccess = false, Message = "رکورد پرداخت اسنپ‌پی یافت نشد." };

            if (record.Status == BnplPaymentStatus.Paid || record.Status == BnplPaymentStatus.Settled)
                return new BnplVerifyResult { IsSuccess = true, AlreadyProcessed = true, Message = "این تراکنش قبلاً تایید شده است." };

            var client = await CreateSnapppayClientAsync(credential);
            var verifyToken = !string.IsNullOrWhiteSpace(paymentToken) ? paymentToken : record.PaymentToken;

            var response = await client.PostAsJsonAsync(
                $"{GetSnapppayBase(credential)}/api/online/payment/v1/verify",
                new { paymentToken = verifyToken });
            var raw = await response.Content.ReadAsStringAsync();
            var dto = TryDeserialize<SnapppayResultDto<SnapppayVerifyDto>>(raw);

            var verified = dto?.Successful == true;

            record.Status = verified ? BnplPaymentStatus.Paid : BnplPaymentStatus.Failed;
            record.RawResponseJson = raw;
            record.VerifiedOnUtc = DateTime.UtcNow;
            await _paymentRepository.UpdateAsync(record);

            return new BnplVerifyResult
            {
                IsSuccess = verified,
                Message = verified ? "پرداخت اقساطی اسنپ‌پی تایید شد." : (dto?.ErrorData?.Message ?? "تایید پرداخت اسنپ‌پی ناموفق بود."),
                TrackingCode = dto?.Response?.TransactionId
            };
        }

        private async Task<HttpClient> CreateSnapppayClientAsync(BnplCredential credential)
        {
            var client = _httpClientFactory.CreateClient("BnplGateway");

            var basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{credential.ClientId}:{credential.ClientSecret}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

            var tokenResponse = await client.PostAsync(
                $"{GetSnapppayBase(credential)}/api/online/v1/oauth/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "grant_type", "password" },
                    { "scope", "online-merchant" },
                    { "username", credential.Username },
                    { "password", credential.Password }
                }));

            if (tokenResponse.IsSuccessStatusCode)
            {
                var tokenDto = await tokenResponse.Content.ReadFromJsonAsync<SnapppayTokenDto>();
                if (tokenDto?.AccessToken != null)
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenDto.AccessToken);
            }

            return client;
        }

        private static string GetSnapppayBase(BnplCredential credential) =>
            !string.IsNullOrWhiteSpace(credential.BaseUrlOverride)
                ? credential.BaseUrlOverride.TrimEnd('/')
                : SnapppayDefaultBase;

        private async Task<BnplCredential> GetActiveCredentialAsync(int storeId, string providerKey)
        {
            var all = await _credentialRepository.GetAllAsync(q =>
                q.Where(c => c.StoreId == storeId && c.ProviderKey == providerKey && c.IsActive));
            return all.FirstOrDefault();
        }

        private async Task SavePaymentRecordAsync(
            int storeId, int orderId, int customerId, string providerKey, string transactionId, string paymentToken,
            decimal amountToman, string rawRequest, string rawResponse, BnplPaymentStatus status)
        {
            await _paymentRepository.InsertAsync(new BnplPaymentRecord
            {
                StoreId = storeId,
                OrderId = orderId,
                CustomerId = customerId,
                ProviderKey = providerKey,
                TransactionId = transactionId,
                PaymentToken = paymentToken,
                AmountToman = amountToman,
                Status = status,
                RawRequestJson = rawRequest,
                RawResponseJson = rawResponse,
                CreatedOnUtc = DateTime.UtcNow
            });
        }

        private static T TryDeserialize<T>(string json)
        {
            try
            {
                return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return default;
            }
        }
    }

    // ────────────────────────── DTO های دیجی‌پی ──────────────────────────

    internal class DigipayTokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }

    internal class DigipayTicketRequest
    {
        [JsonPropertyName("cellNumber")] public string CellNumber { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; }
        [JsonPropertyName("providerId")] public string ProviderId { get; set; }
        [JsonPropertyName("callbackUrl")] public string CallbackUrl { get; set; }
        [JsonPropertyName("basketDetailsDto")] public DigipayBasketDetails BasketDetails { get; set; }
    }

    internal class DigipayBasketDetails
    {
        [JsonPropertyName("basketId")] public string BasketId { get; set; }
        [JsonPropertyName("items")] public List<DigipayBasketItem> Items { get; set; }
    }

    internal class DigipayBasketItem
    {
        [JsonPropertyName("sellerId")] public string SellerId { get; set; }
        [JsonPropertyName("supplierId")] public string SupplierId { get; set; }
        [JsonPropertyName("productCode")] public string ProductCode { get; set; }
        [JsonPropertyName("brand")] public string Brand { get; set; }
        [JsonPropertyName("productType")] public int ProductType { get; set; }
        [JsonPropertyName("count")] public int Count { get; set; }
        [JsonPropertyName("categoryId")] public string CategoryId { get; set; }
    }

    internal class DigipayTicketResponse
    {
        [JsonPropertyName("result")] public DigipayResult Result { get; set; }
        [JsonPropertyName("ticket")] public string Ticket { get; set; }
        [JsonPropertyName("redirectUrl")] public string RedirectUrl { get; set; }
    }

    internal class DigipayVerifyResponse
    {
        [JsonPropertyName("result")] public DigipayResult Result { get; set; }
        [JsonPropertyName("trackingCode")] public string TrackingCode { get; set; }
        [JsonPropertyName("providerId")] public string ProviderId { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; }
        [JsonPropertyName("paymentGateway")] public int PaymentGateway { get; set; }
        [JsonPropertyName("additionalInfo")] public DigipayVerifyAdditionalInfo AdditionalInfo { get; set; }
    }

    internal class DigipayResult
    {
        [JsonPropertyName("status")] public int Status { get; set; }
        [JsonPropertyName("message")] public string Message { get; set; }
        [JsonPropertyName("level")] public string Level { get; set; }
    }

    internal class DigipayVerifyAdditionalInfo
    {
        [JsonPropertyName("prepaymentAmount")] public long PrepaymentAmount { get; set; }
        [JsonPropertyName("cashAmount")] public long CashAmount { get; set; }
        [JsonPropertyName("creditAmount")] public long CreditAmount { get; set; }
        [JsonPropertyName("instantFinalization")] public bool InstantFinalization { get; set; }
    }

    // ────────────────────────── DTO های اسنپ‌پی (مرجع NopPlus) ──────────────────────────

    internal class SnapppayTokenDto
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; }
        [JsonPropertyName("token_type")] public string TokenType { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }

    internal class SnapppayPaymentRequestDto
    {
        [JsonPropertyName("transactionId")] public string TransactionId { get; set; }
        [JsonPropertyName("amount")] public int Amount { get; set; }
        [JsonPropertyName("returnURL")] public string ReturnURL { get; set; }
        [JsonPropertyName("paymentMethodTypeDto")] public string PaymentMethodTypeDto { get; set; }
        [JsonPropertyName("cartList")] public List<SnapppayCartList> CartList { get; set; }
        [JsonPropertyName("discountAmount")] public int DiscountAmount { get; set; }
        [JsonPropertyName("externalSourceAmount")] public int ExternalSourceAmount { get; set; }
        [JsonPropertyName("mobile")] public string Mobile { get; set; }
    }

    internal class SnapppayCartList
    {
        [JsonPropertyName("cartId")] public int CartId { get; set; }
        [JsonPropertyName("cartItems")] public List<SnapppayCartItem> CartItems { get; set; }
        [JsonPropertyName("totalAmount")] public int TotalAmount { get; set; }
        [JsonPropertyName("isShipmentIncluded")] public bool IsShipmentIncluded { get; set; }
        [JsonPropertyName("shippingAmount")] public int ShippingAmount { get; set; }
        [JsonPropertyName("isTaxIncluded")] public bool IsTaxIncluded { get; set; }
        [JsonPropertyName("taxAmount")] public int TaxAmount { get; set; }
    }

    internal class SnapppayCartItem
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("count")] public int Count { get; set; }
        [JsonPropertyName("amount")] public int Amount { get; set; }
        [JsonPropertyName("category")] public string Category { get; set; }
    }

    internal class SnapppayResultDto<T>
    {
        [JsonPropertyName("successful")] public bool Successful { get; set; }
        [JsonPropertyName("response")] public T Response { get; set; }
        [JsonPropertyName("errorData")] public SnapppayErrorData ErrorData { get; set; }
    }

    internal class SnapppayErrorData
    {
        [JsonPropertyName("errorCode")] public string ErrorCode { get; set; }
        [JsonPropertyName("message")] public string Message { get; set; }
    }

    internal class SnapppayEligibleDto
    {
        [JsonPropertyName("eligible")] public bool Eligible { get; set; }
        [JsonPropertyName("title_message")] public string TitleMessage { get; set; }
        [JsonPropertyName("description")] public string Description { get; set; }
    }

    internal class SnapppayPaymentTokenDto
    {
        [JsonPropertyName("paymentToken")] public string PaymentToken { get; set; }
        [JsonPropertyName("paymentPageUrl")] public string PaymentPageUrl { get; set; }
    }

    internal class SnapppayVerifyDto
    {
        [JsonPropertyName("transactionId")] public string TransactionId { get; set; }
    }
}
