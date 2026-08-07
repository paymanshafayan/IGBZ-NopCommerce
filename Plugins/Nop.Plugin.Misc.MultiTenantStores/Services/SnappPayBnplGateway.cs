namespace Nop.Plugin.Misc.MultiTenantStores.Services
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;

    /// <summary>
    /// یکپارچه‌سازی اعتبارسنجی اقساط (SnappPay/Digipay/Tara BNPL) (.NET 9)
    /// نکته: استعلام امکان‌سنجی اعتبار مشتری («آیا این مشتری صلاحیت اعتباری این مبلغ را دارد؟»)
    /// همیشه باید از سرویس اعتبارسنجی واقعی بانک/فینتک استعلام شود؛ محاسبه محلی صرفاً برای
    /// نمایش جدول اقساط پیشنهادی است و به‌تنهایی هرگز نباید معیار تایید نهایی خرید اعتباری باشد.
    /// </summary>
    public class SnappPayBnplGateway
    {
        private readonly HttpClient _httpClient;

        public SnappPayBnplGateway(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<BnplEligibilityResult> CheckEligibilityAndInstallmentsAsync(string merchantApiKey, decimal cartTotalToman, string customerNationalId, string customerMobile)
        {
            if (string.IsNullOrWhiteSpace(merchantApiKey))
            {
                return new BnplEligibilityResult
                {
                    IsEligible = false,
                    Message = "کلید API اسنپ‌پِی/دیجی‌پِی برای این فروشگاه فعال نشده است."
                };
            }

            if (string.IsNullOrWhiteSpace(customerNationalId) || string.IsNullOrWhiteSpace(customerMobile))
            {
                return new BnplEligibilityResult
                {
                    IsEligible = false,
                    Message = "برای استعلام اعتبار BNPL، کدملی و شماره موبایل مشتری الزامی است."
                };
            }

            _httpClient.DefaultRequestHeaders.Remove("Authorization");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {merchantApiKey}");

            SnappPayEligibilityApiResponse apiResponse;
            try
            {
                var response = await _httpClient.PostAsJsonAsync("https://api.snapppay.local/v1/eligibility/check", new SnappPayEligibilityApiRequest
                {
                    NationalId = customerNationalId,
                    Mobile = customerMobile,
                    AmountRials = cartTotalToman * 10
                });

                if (!response.IsSuccessStatusCode)
                {
                    return new BnplEligibilityResult
                    {
                        IsEligible = false,
                        Message = $"سرویس اعتبارسنجی BNPL درخواست را رد کرد (کد {(int)response.StatusCode})."
                    };
                }

                apiResponse = await response.Content.ReadFromJsonAsync<SnappPayEligibilityApiResponse>();
            }
            catch (HttpRequestException ex)
            {
                return new BnplEligibilityResult
                {
                    IsEligible = false,
                    Message = $"ارتباط با سرویس اعتبارسنجی BNPL برقرار نشد: {ex.Message}"
                };
            }

            if (apiResponse == null || !apiResponse.Eligible)
            {
                return new BnplEligibilityResult
                {
                    IsEligible = false,
                    Message = apiResponse?.RejectionReason ?? "مشتری صلاحیت لازم برای خرید اقساطی این مبلغ را ندارد."
                };
            }

            var installmentCount = Math.Max(1, apiResponse.ApprovedInstallmentCount);
            var installmentAmount = Math.Round(cartTotalToman / installmentCount, 0);
            var installments = new List<BnplInstallmentItem>();
            for (var i = 0; i < installmentCount; i++)
            {
                installments.Add(new BnplInstallmentItem
                {
                    Title = i == 0 ? "قسط اول (امروز هنگام خرید)" : $"قسط {i + 1}",
                    Amount = installmentAmount,
                    DueDateDescription = i == 0 ? "پرداخت آنی" : $"{i} ماه آینده"
                });
            }

            return new BnplEligibilityResult
            {
                IsEligible = true,
                ApprovalReferenceId = apiResponse.ApprovalReferenceId,
                MonthlyInstallmentAmount = installmentAmount,
                Installments = installments,
                Message = $"امکان پرداخت اقساطی {installmentCount} قسطه برای این سبد خرید تایید شد."
            };
        }
    }

    public class BnplEligibilityResult
    {
        public bool IsEligible { get; set; }
        public string ApprovalReferenceId { get; set; }
        public decimal MonthlyInstallmentAmount { get; set; }
        public List<BnplInstallmentItem> Installments { get; set; }
        public string Message { get; set; }
    }

    public class BnplInstallmentItem
    {
        public string Title { get; set; }
        public decimal Amount { get; set; }
        public string DueDateDescription { get; set; }
    }

    internal class SnappPayEligibilityApiRequest
    {
        [JsonPropertyName("nationalId")] public string NationalId { get; set; }
        [JsonPropertyName("mobile")] public string Mobile { get; set; }
        [JsonPropertyName("amountRials")] public decimal AmountRials { get; set; }
    }

    internal class SnappPayEligibilityApiResponse
    {
        [JsonPropertyName("eligible")] public bool Eligible { get; set; }
        [JsonPropertyName("approvedInstallmentCount")] public int ApprovedInstallmentCount { get; set; }
        [JsonPropertyName("approvalReferenceId")] public string ApprovalReferenceId { get; set; }
        [JsonPropertyName("rejectionReason")] public string RejectionReason { get; set; }
    }
}
