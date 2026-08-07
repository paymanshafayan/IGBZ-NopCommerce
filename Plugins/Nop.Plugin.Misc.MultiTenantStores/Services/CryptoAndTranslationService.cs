namespace Nop.Plugin.Misc.MultiTenantStores.Services
{
    using System;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Net.Http.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;

    /// <summary>
    /// NOWPayments Crypto Gateway (USDT) & Auto Translation Service (.NET 9)
    /// نکته: آدرس کیف‌پول و متن ترجمه‌شده هرگز نباید مقدار ثابت (Hardcoded) باشند — این مقادیر
    /// همیشه باید از پاسخ واقعی API بیرونی خوانده شوند، چون به ازای هر سفارش/محصول متفاوت‌اند.
    /// </summary>
    public interface ICryptoAndTranslationService
    {
        Task<CryptoInvoiceResult> CreateNowPaymentsInvoiceAsync(string nowPaymentsApiKey, decimal priceUsd, string orderId, string callbackUrl);
        Task<TranslatedProductDto> AutoTranslateProductCatalogAsync(string translationApiKey, string sourceName, string sourceDescription, string targetLanguageCode);
    }

    public class CryptoAndTranslationService : ICryptoAndTranslationService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public CryptoAndTranslationService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// ایجاد فاکتور پرداخت رمزارزی واقعی (تتر USDT-TRC20) از طریق NOWPayments API.
        /// آدرس کیف‌پول و شناسه فاکتور همیشه از پاسخ واقعی سرویس خوانده می‌شود، نه مقدار ثابت.
        /// </summary>
        public async Task<CryptoInvoiceResult> CreateNowPaymentsInvoiceAsync(string nowPaymentsApiKey, decimal priceUsd, string orderId, string callbackUrl)
        {
            if (string.IsNullOrWhiteSpace(nowPaymentsApiKey))
            {
                return new CryptoInvoiceResult { IsSuccess = false, Message = "کلید API درگاه رمزارزی NOWPayments تنظیم نشده است." };
            }

            var httpClient = _httpClientFactory.CreateClient("NowPayments");
            httpClient.DefaultRequestHeaders.Remove("x-api-key");
            httpClient.DefaultRequestHeaders.Add("x-api-key", nowPaymentsApiKey);

            try
            {
                var response = await httpClient.PostAsJsonAsync("https://api.nowpayments.io/v1/invoice", new NowPaymentsCreateInvoiceRequest
                {
                    PriceAmount = priceUsd,
                    PriceCurrency = "usd",
                    PayCurrency = "usdttrc20",
                    OrderId = orderId,
                    IpnCallbackUrl = callbackUrl
                });

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    return new CryptoInvoiceResult
                    {
                        IsSuccess = false,
                        Message = $"NOWPayments فاکتور را رد کرد (کد {(int)response.StatusCode}): {errorBody}"
                    };
                }

                var payload = await response.Content.ReadFromJsonAsync<NowPaymentsCreateInvoiceResponse>();
                if (payload == null || string.IsNullOrEmpty(payload.InvoiceUrl))
                {
                    return new CryptoInvoiceResult { IsSuccess = false, Message = "پاسخ نامعتبر از NOWPayments دریافت شد." };
                }

                return new CryptoInvoiceResult
                {
                    IsSuccess = true,
                    InvoiceId = payload.Id,
                    PaymentUrl = payload.InvoiceUrl,
                    Message = "فاکتور پرداخت رمزارزی (USDT-TRC20) با موفقیت ایجاد شد."
                };
            }
            catch (HttpRequestException ex)
            {
                return new CryptoInvoiceResult { IsSuccess = false, Message = $"ارتباط با NOWPayments برقرار نشد: {ex.Message}" };
            }
        }

        /// <summary>
        /// ترجمه واقعی نام و توضیحات محصول از طریق سرویس ترجمه بیرونی (ترجمیار/فرازین یا هر
        /// ارائه‌دهنده سازگار با API). خروجی همیشه بازتاب متن ورودی واقعی است، نه رشته ثابت نمونه.
        /// </summary>
        public async Task<TranslatedProductDto> AutoTranslateProductCatalogAsync(string translationApiKey, string sourceName, string sourceDescription, string targetLanguageCode)
        {
            if (string.IsNullOrWhiteSpace(translationApiKey))
            {
                return new TranslatedProductDto
                {
                    IsSuccess = false,
                    LanguageCode = targetLanguageCode,
                    Message = "کلید API سرویس ترجمه برای این فروشگاه تنظیم نشده است."
                };
            }

            var httpClient = _httpClientFactory.CreateClient("TranslationProvider");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", translationApiKey);

            try
            {
                var response = await httpClient.PostAsJsonAsync("https://api.translation-provider.local/v1/translate", new TranslationApiRequest
                {
                    TargetLanguage = targetLanguageCode,
                    Fields = new[] { sourceName, sourceDescription }
                });

                if (!response.IsSuccessStatusCode)
                {
                    return new TranslatedProductDto
                    {
                        IsSuccess = false,
                        LanguageCode = targetLanguageCode,
                        Message = $"سرویس ترجمه پاسخ ناموفق داد (کد {(int)response.StatusCode})."
                    };
                }

                var payload = await response.Content.ReadFromJsonAsync<TranslationApiResponse>();
                if (payload?.TranslatedFields == null || payload.TranslatedFields.Length < 2)
                {
                    return new TranslatedProductDto { IsSuccess = false, LanguageCode = targetLanguageCode, Message = "پاسخ نامعتبر از سرویس ترجمه." };
                }

                return new TranslatedProductDto
                {
                    IsSuccess = true,
                    LanguageCode = targetLanguageCode,
                    TranslatedName = payload.TranslatedFields[0],
                    TranslatedDescription = payload.TranslatedFields[1],
                    Message = "ترجمه با موفقیت انجام شد."
                };
            }
            catch (HttpRequestException ex)
            {
                return new TranslatedProductDto { IsSuccess = false, LanguageCode = targetLanguageCode, Message = $"ارتباط با سرویس ترجمه برقرار نشد: {ex.Message}" };
            }
        }
    }

    public class CryptoInvoiceResult
    {
        public bool IsSuccess { get; set; }
        public string InvoiceId { get; set; }
        public string PaymentUrl { get; set; }
        public string Message { get; set; }
    }

    public class TranslatedProductDto
    {
        public bool IsSuccess { get; set; }
        public string LanguageCode { get; set; }
        public string TranslatedName { get; set; }
        public string TranslatedDescription { get; set; }
        public string Message { get; set; }
    }

    internal class NowPaymentsCreateInvoiceRequest
    {
        [JsonPropertyName("price_amount")] public decimal PriceAmount { get; set; }
        [JsonPropertyName("price_currency")] public string PriceCurrency { get; set; }
        [JsonPropertyName("pay_currency")] public string PayCurrency { get; set; }
        [JsonPropertyName("order_id")] public string OrderId { get; set; }
        [JsonPropertyName("ipn_callback_url")] public string IpnCallbackUrl { get; set; }
    }

    internal class NowPaymentsCreateInvoiceResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("invoice_url")] public string InvoiceUrl { get; set; }
    }

    internal class TranslationApiRequest
    {
        [JsonPropertyName("targetLanguage")] public string TargetLanguage { get; set; }
        [JsonPropertyName("fields")] public string[] Fields { get; set; }
    }

    internal class TranslationApiResponse
    {
        [JsonPropertyName("translatedFields")] public string[] TranslatedFields { get; set; }
    }
}
