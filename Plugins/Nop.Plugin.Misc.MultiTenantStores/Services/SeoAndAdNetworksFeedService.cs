namespace Nop.Plugin.Misc.MultiTenantStores.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Json;
    using System.Text;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using System.Xml.Linq;
    using Nop.Core;
    using Nop.Services.Catalog;
    using Nop.Services.Media;
    using Nop.Services.Seo;

    /// <summary>
    /// SEO Meta Generation, Yektanet/Tapsell Product Feed & Triboon Advertorial Service (.NET 9)
    /// </summary>
    public interface ISeoAndAdNetworksFeedService
    {
        Task<SeoMetaResult> GenerateProductSeoMetaAsync(string productName, string rawDescription);
        Task<string> GenerateYektanetRetargetingXmlFeedAsync(int storeId);
        Task<TriboonOrderResult> PublishAdvertorialOnTriboonAsync(string triboonApiKey, string campaignTitle, string articleBodyHtml, List<string> targetMedia);
    }

    public class SeoAndAdNetworksFeedService : ISeoAndAdNetworksFeedService
    {
        private readonly IProductService _productService;
        private readonly IPictureService _pictureService;
        private readonly IUrlRecordService _urlRecordService;
        private readonly IWebHelper _webHelper;
        private readonly IHttpClientFactory _httpClientFactory;

        public SeoAndAdNetworksFeedService(
            IProductService productService,
            IPictureService pictureService,
            IUrlRecordService urlRecordService,
            IWebHelper webHelper,
            IHttpClientFactory httpClientFactory)
        {
            _productService = productService;
            _pictureService = pictureService;
            _urlRecordService = urlRecordService;
            _webHelper = webHelper;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// تولید خودکار Meta Title، Meta Description و هشتگ (قالب‌بندی قطعی بر اساس متن ورودی واقعی)
        /// </summary>
        public async Task<SeoMetaResult> GenerateProductSeoMetaAsync(string productName, string rawDescription)
        {
            if (string.IsNullOrWhiteSpace(productName))
                throw new ArgumentException("نام محصول برای تولید متادیتای سئو الزامی است.", nameof(productName));

            var safeDescription = rawDescription ?? string.Empty;
            var metaTitle = $"{productName} | خرید آنلاین با بهترین قیمت و ارسال سریع";
            var metaDescription = $"خرید اینترنتی {productName}. {safeDescription.Substring(0, Math.Min(100, safeDescription.Length))}... ضمانت اصالت و بازگشت ۷ روزه.";
            var hashtags = $"#{productName.Replace(" ", "_")} #خرید_آنلاین #فروشگاه_اینترنتی";

            return await Task.FromResult(new SeoMetaResult
            {
                MetaTitle = metaTitle,
                MetaDescription = metaDescription,
                Hashtags = hashtags
            });
        }

        /// <summary>
        /// فید زنده RSS محصولات برای یکتانت/تپسل، ساخته‌شده از کاتالوگ واقعی فروشگاه
        /// (نه یک آیتم ثابت نمونه).
        /// </summary>
        public async Task<string> GenerateYektanetRetargetingXmlFeedAsync(int storeId)
        {
            var products = await _productService.SearchProductsAsync(storeId: storeId, visibleIndividuallyOnly: true, pageSize: 500);
            var storeBaseUrl = _webHelper.GetStoreLocation();

            var channel = new XElement("channel",
                new XElement("title", "Yektanet Retargeting Product Feed"));

            foreach (var product in products)
            {
                var seName = await _urlRecordService.GetSeNameAsync(product);
                var pictures = await _pictureService.GetPicturesByProductIdAsync(product.Id, 1);
                var picture = pictures.FirstOrDefault();
                string imageUrl = string.Empty;
                if (picture != null)
                {
                    var result = await _pictureService.GetPictureUrlAsync(picture);
                    imageUrl = result.Url;
                }

                channel.Add(new XElement("item",
                    new XElement("id", product.Id),
                    new XElement("title", product.Name),
                    new XElement("price", product.Price),
                    new XElement("link", $"{storeBaseUrl.TrimEnd('/')}/{seName}"),
                    new XElement("image_link", imageUrl)));
            }

            var document = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("rss", new XAttribute("version", "2.0"), channel));

            return document.ToString(SaveOptions.DisableFormatting);
        }

        /// <summary>
        /// سفارش رپورتاژ آگهی از طریق Triboon API — نتیجه از پاسخ واقعی سرویس خوانده می‌شود.
        /// </summary>
        public async Task<TriboonOrderResult> PublishAdvertorialOnTriboonAsync(string triboonApiKey, string campaignTitle, string articleBodyHtml, List<string> targetMedia)
        {
            if (string.IsNullOrWhiteSpace(triboonApiKey))
            {
                return new TriboonOrderResult { IsSuccess = false, Message = "کلید API تریبون برای این فروشگاه تنظیم نشده است." };
            }

            var httpClient = _httpClientFactory.CreateClient("TriboonApi");
            httpClient.DefaultRequestHeaders.Remove("Authorization");
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {triboonApiKey}");

            try
            {
                var response = await httpClient.PostAsJsonAsync("https://api.triboon.co/v1/campaigns", new TriboonCampaignRequest
                {
                    Title = campaignTitle,
                    ArticleHtml = articleBodyHtml,
                    TargetMedia = targetMedia ?? new List<string>()
                });

                var rawBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    return new TriboonOrderResult { IsSuccess = false, Message = $"سفارش تریبون رد شد (کد {(int)response.StatusCode}): {rawBody}" };
                }

                var payload = await response.Content.ReadFromJsonAsync<TriboonCampaignResponse>();
                return new TriboonOrderResult
                {
                    IsSuccess = true,
                    CampaignId = payload?.CampaignId,
                    PublishedMediaCount = payload?.AcceptedMediaCount ?? 0,
                    Message = "رپورتاژ آگهی با موفقیت در کمپین تریبون ثبت و به رسانه‌ها ارسال شد."
                };
            }
            catch (HttpRequestException ex)
            {
                return new TriboonOrderResult { IsSuccess = false, Message = $"ارتباط با API تریبون برقرار نشد: {ex.Message}" };
            }
        }
    }

    public class SeoMetaResult
    {
        public string MetaTitle { get; set; }
        public string MetaDescription { get; set; }
        public string Hashtags { get; set; }
    }

    public class TriboonOrderResult
    {
        public bool IsSuccess { get; set; }
        public string CampaignId { get; set; }
        public int PublishedMediaCount { get; set; }
        public string Message { get; set; }
    }

    internal class TriboonCampaignRequest
    {
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("article_html")] public string ArticleHtml { get; set; }
        [JsonPropertyName("target_media")] public List<string> TargetMedia { get; set; }
    }

    internal class TriboonCampaignResponse
    {
        [JsonPropertyName("campaign_id")] public string CampaignId { get; set; }
        [JsonPropertyName("accepted_media_count")] public int AcceptedMediaCount { get; set; }
    }
}
