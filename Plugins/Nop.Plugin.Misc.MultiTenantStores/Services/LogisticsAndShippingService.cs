namespace Nop.Plugin.Misc.MultiTenantStores.Services
{
    using System;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Security.Cryptography;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;

    /// <summary>
    /// Logistics, Courier Routing & Delivery PIN Verification Service (.NET 9 / nopCommerce 4.90)
    /// </summary>
    public interface ILogisticsAndShippingService
    {
        RouteCategoryResult CategorizeShipmentRoute(decimal weightKg, string destinationCity, bool isExpressNeeded);
        string GenerateDeliveryPin();
        Task<TapinShipmentResult> RegisterTapinPostShipmentAsync(string tapinApiKey, int orderId, string recipientAddress, string recipientPhone, bool isCod);
    }

    public class LogisticsAndShippingService : ILogisticsAndShippingService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public LogisticsAndShippingService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// دسته‌بندی خودکار روش ارسال بر اساس وزن، ابعاد و شهر مقصد
        /// </summary>
        public RouteCategoryResult CategorizeShipmentRoute(decimal weightKg, string destinationCity, bool isExpressNeeded)
        {
            if (weightKg > 30)
            {
                return new RouteCategoryResult
                {
                    RouteType = "HEAVY_FREIGHT",
                    CarrierName = "باربری / تیپاژ سنگین",
                    EstimatedCostToman = 150000,
                    DeliveryPINRequired = true
                };
            }

            if (destinationCity.Equals("تهران", StringComparison.OrdinalIgnoreCase) || isExpressNeeded)
            {
                return new RouteCategoryResult
                {
                    RouteType = "EXPRESS_COURIER",
                    CarrierName = "اسنپ‌باکس / الوپیک (ارسال فوری درون‌شهری)",
                    EstimatedCostToman = 65000,
                    DeliveryPINRequired = true
                };
            }

            return new RouteCategoryResult
            {
                RouteType = "NATIONAL_POST",
                CarrierName = "پست پیشتاز (اتصال تاپین / پستکس)",
                EstimatedCostToman = 45000,
                DeliveryPINRequired = false
            };
        }

        /// <summary>
        /// تولید کد OTP ۴ رقمی تحویل کالا با تولیدکننده تصادفی رمزنگارانه (نه Random(seed) قابل‌حدس).
        /// </summary>
        public string GenerateDeliveryPin()
        {
            var value = RandomNumberGenerator.GetInt32(1000, 10000);
            return value.ToString();
        }

        /// <summary>
        /// ثبت واقعی مرسوله در سامانه تاپین/پستکس از طریق فراخوانی HTTP؛ کد پیگیری از پاسخ واقعی
        /// API خوانده می‌شود، نه یک رشته ساختگی محلی.
        /// </summary>
        public async Task<TapinShipmentResult> RegisterTapinPostShipmentAsync(string tapinApiKey, int orderId, string recipientAddress, string recipientPhone, bool isCod)
        {
            if (string.IsNullOrWhiteSpace(tapinApiKey))
            {
                return new TapinShipmentResult { IsSuccess = false, Message = "کلید API تاپین/پستکس برای این فروشگاه تنظیم نشده است." };
            }

            var deliveryPin = GenerateDeliveryPin();
            var httpClient = _httpClientFactory.CreateClient("TapinPost");
            httpClient.DefaultRequestHeaders.Remove("Authorization");
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {tapinApiKey}");

            try
            {
                var response = await httpClient.PostAsJsonAsync("https://api.tapin.ir/v1/shipments", new TapinCreateShipmentRequest
                {
                    ExternalOrderId = orderId.ToString(),
                    RecipientAddress = recipientAddress,
                    RecipientPhone = recipientPhone,
                    IsCashOnDelivery = isCod,
                    DeliveryPin = deliveryPin
                });

                var rawBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    return new TapinShipmentResult { IsSuccess = false, Message = $"ثبت مرسوله در تاپین ناموفق بود (کد {(int)response.StatusCode}): {rawBody}" };
                }

                var payload = await response.Content.ReadFromJsonAsync<TapinCreateShipmentResponse>();
                if (payload == null || string.IsNullOrEmpty(payload.TrackingCode))
                {
                    return new TapinShipmentResult { IsSuccess = false, Message = "پاسخ نامعتبر از سامانه تاپین دریافت شد." };
                }

                return new TapinShipmentResult
                {
                    IsSuccess = true,
                    PostTrackingCode = payload.TrackingCode,
                    DeliveryPin = deliveryPin,
                    BarcodeImageUrl = payload.BarcodeUrl,
                    Message = $"مرسوله با موفقیت در پست تاپین ثبت شد. کد پیگیری: {payload.TrackingCode}"
                };
            }
            catch (HttpRequestException ex)
            {
                return new TapinShipmentResult { IsSuccess = false, Message = $"ارتباط با سامانه تاپین برقرار نشد: {ex.Message}" };
            }
        }
    }

    public class RouteCategoryResult
    {
        public string RouteType { get; set; }
        public string CarrierName { get; set; }
        public decimal EstimatedCostToman { get; set; }
        public bool DeliveryPINRequired { get; set; }
    }

    public class TapinShipmentResult
    {
        public bool IsSuccess { get; set; }
        public string PostTrackingCode { get; set; }
        public string DeliveryPin { get; set; }
        public string BarcodeImageUrl { get; set; }
        public string Message { get; set; }
    }

    internal class TapinCreateShipmentRequest
    {
        [JsonPropertyName("external_order_id")] public string ExternalOrderId { get; set; }
        [JsonPropertyName("recipient_address")] public string RecipientAddress { get; set; }
        [JsonPropertyName("recipient_phone")] public string RecipientPhone { get; set; }
        [JsonPropertyName("is_cod")] public bool IsCashOnDelivery { get; set; }
        [JsonPropertyName("delivery_pin")] public string DeliveryPin { get; set; }
    }

    internal class TapinCreateShipmentResponse
    {
        [JsonPropertyName("tracking_code")] public string TrackingCode { get; set; }
        [JsonPropertyName("barcode_url")] public string BarcodeUrl { get; set; }
    }
}
