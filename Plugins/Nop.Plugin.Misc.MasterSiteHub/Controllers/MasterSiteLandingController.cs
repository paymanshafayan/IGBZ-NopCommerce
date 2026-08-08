namespace Nop.Plugin.Misc.MasterSiteHub.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Core.Domain.Orders;
    using Nop.Services.Catalog;
    using Nop.Services.Media;
    using Nop.Services.Orders;
    using Nop.Services.Stores;
    using Nop.Plugin.Misc.MultiTenantStores.Services;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;

    [ApiController]
    [Route("api/mastersite/public")]
    [Microsoft.AspNetCore.Cors.EnableCors("MasterSitePublicApi")]
    public class MasterSiteLandingController : ControllerBase
    {
        private readonly IStoreService _storeService;
        private readonly ITenantPlanService _tenantPlanService;
        private readonly IStoreDomainMappingService _domainMappingService;
        private readonly ITenantProvisioningService _tenantProvisioningService;
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;
        private readonly IPictureService _pictureService;
        private readonly IOrderService _orderService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IParbadPaymentService _paymentService;
        private readonly IJwtTokenService _jwtTokenService;
        private readonly ILandingContentBlockService _contentBlockService;

        public MasterSiteLandingController(
            IStoreService storeService,
            ITenantPlanService tenantPlanService,
            IStoreDomainMappingService domainMappingService,
            ITenantProvisioningService tenantProvisioningService,
            IProductService productService,
            ICategoryService categoryService,
            IPictureService pictureService,
            IOrderService orderService,
            IOrderProcessingService orderProcessingService,
            IParbadPaymentService paymentService,
            IJwtTokenService jwtTokenService,
            ILandingContentBlockService contentBlockService)
        {
            _storeService = storeService;
            _tenantPlanService = tenantPlanService;
            _domainMappingService = domainMappingService;
            _tenantProvisioningService = tenantProvisioningService;
            _productService = productService;
            _categoryService = categoryService;
            _pictureService = pictureService;
            _orderService = orderService;
            _orderProcessingService = orderProcessingService;
            _paymentService = paymentService;
            _jwtTokenService = jwtTokenService;
            _contentBlockService = contentBlockService;
        }

        /// <summary>
        /// دریافت داده‌های صفحه اصلی سایت مادر (Hero, Stories اینستاگرامی، پلن‌ها، آمار کلی).
        /// ⚠️ نسخهٔ قبلی این متد دقیقاً همان الگوی باگ «داده فرضی به‌جای واقعی» را داشت که قبلاً در
        /// MasterSiteAdminController پیدا و رفع شده بود: آواتار همیشه یک عکس ثابت Unsplash،
        /// CategoryName بر اساس زوج/فرد بودن اندیس (نه دستهٔ واقعی محصول)، HasActiveStory همیشه
        /// true، ProductPreviewCount از یک فرمول ساختگی (index*5)، و TotalOrdersProcessed/
        /// PlatformUptime/AverageSetupTimeMinutes همگی عدد ثابت Hardcode — همه اینجا با دادهٔ واقعی
        /// جایگزین یا (وقتی منبع دادهٔ واقعی وجود نداشت) کاملاً حذف شدند.
        /// </summary>
        [HttpGet("landing-data")]
        public async Task<IActionResult> GetLandingData()
        {
            var allStores = await _storeService.GetAllStoresAsync();
            var topStores = allStores.Take(8).ToList();

            var instagramStories = new List<object>();
            foreach (var store in topStores)
            {
                // به‌جای آواتار عمومی ثابت، اولین عکس اولین محصول قابل‌مشاهدهٔ همان فروشگاه (اگر
                // وجود داشته باشد) به‌عنوان تصویر معرف واقعی استفاده می‌شود.
                var storeProducts = await _productService.SearchProductsAsync(
                    storeId: store.Id, visibleIndividuallyOnly: true, pageSize: 1);

                string avatarUrl = null;
                string categoryName = null;

                var representativeProduct = storeProducts.FirstOrDefault();
                if (representativeProduct != null)
                {
                    var pictures = await _pictureService.GetPicturesByProductIdAsync(representativeProduct.Id, 1);
                    var firstPicture = pictures.FirstOrDefault();
                    if (firstPicture != null)
                        (avatarUrl, _) = await _pictureService.GetPictureUrlAsync(firstPicture);

                    var productCategories = await _categoryService.GetProductCategoriesByProductIdAsync(representativeProduct.Id);
                    var firstCategoryMapping = productCategories.FirstOrDefault();
                    if (firstCategoryMapping != null)
                    {
                        var category = await _categoryService.GetCategoryByIdAsync(firstCategoryMapping.CategoryId);
                        categoryName = category?.Name;
                    }
                }

                instagramStories.Add(new
                {
                    StoreId = store.Id,
                    StoreName = store.Name,
                    AvatarUrl = avatarUrl, // اگر null باشد، فرانت باید آیکون پیش‌فرض نشان دهد، نه عکس ساختگی
                    CategoryName = categoryName, // اگر null باشد یعنی این فروشگاه هنوز محصول دسته‌بندی‌شده ندارد
                    Subdomain = store.Hosts?.Split(',').FirstOrDefault() ?? $"{store.Name.ToLower()}.market.com",
                    ProductPreviewCount = storeProducts.TotalCount // شمارش واقعی، نه فرمول ساختگی
                });
            }

            var activePlans = await _tenantPlanService.GetAllActivePlansAsync();

            // آمار کلی — فقط مقادیری که واقعاً از دیتابیس قابل‌محاسبه‌اند نمایش داده می‌شوند.
            // PlatformUptime و AverageSetupTimeMinutes حذف شدند چون هیچ سیستم مانیتورینگ واقعی برای
            // اندازه‌گیری‌شان در این کدبیس وجود ندارد؛ اگر برای بازاریابی لازم‌اند، باید یا از یک
            // سرویس مانیتورینگ واقعی (مثل UptimeRobot) خوانده شوند یا به‌عنوان متن قابل‌ویرایش ادمین
            // (نه عدد الگوریتمی) در تنظیمات ذخیره شوند.
            var allOrders = await _orderService.SearchOrdersAsync(pageSize: 1);

            var stats = new
            {
                TotalActiveStores = allStores.Count,
                TotalOrdersProcessed = allOrders.TotalCount
            };

            return Ok(new
            {
                HeroTitle = "ساخت فروشگاه اینستاگرامی و اختصاصی در ۱ دقیقه",
                HeroDescription = "بدون نیاز به دانش فنی، فروشگاه چندزبانه با دامنه اختصاصی، وب‌سایت و اپلیکیشن فلاتر خود را تحویل بگیرید.",
                Stories = instagramStories,
                Plans = activePlans,
                Statistics = stats
            });
        }

        /// <summary>
        /// ثبت‌نام مستقیم و واقعی فروشنده: ساخت حساب کاربری + فروشگاه + سفارش اشتراک (اگر پلن رایگان
        /// نباشد، لینک پرداخت هم برمی‌گردد). این Endpoint دقیقاً همان چیزی است که وب‌سایت جدای
        /// Next.js (طبق تصمیم کاربر برای جداسازی سایت مادر جهت مقاومت در برابر فیلترینگ) باید صدا
        /// بزند — قبل از این، هیچ مسیر واقعی «ثبت‌نام مستقیم» در کل پروژه وجود نداشت؛
        /// TenantProvisioningService.ProvisionNewTenantStoreAsync فقط برای مشتریان از‌قبل‌موجود کار
        /// می‌کرد و PlanId را هرگز واقعاً مصرف نمی‌کرد.
        /// </summary>
        [HttpPost("signup")]
        public async Task<IActionResult> Signup([FromBody] TenantSignupRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.StoreName) || string.IsNullOrWhiteSpace(dto.Subdomain)
                || string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            {
                return BadRequest(new { success = false, message = "نام فروشگاه، زیردامنه، ایمیل و رمز عبور الزامی است." });
            }

            var provisioningResult = await _tenantProvisioningService.ProvisionNewTenantStoreAsync(new ProvisionTenantRequest
            {
                StoreName = dto.StoreName,
                Subdomain = dto.Subdomain,
                AdminEmail = dto.Email,
                AdminPhoneNumber = dto.Phone,
                CompanyName = dto.CompanyName,
                PlanId = dto.PlanId,
                Password = dto.Password
            });

            if (!provisioningResult.Success)
                return BadRequest(new { success = false, message = provisioningResult.ErrorMessage });

            var plan = await _tenantPlanService.GetPlanByIdAsync(dto.PlanId);
            var billingCycle = ParseBillingCycle(dto.BillingCycle);
            var planCost = plan == null ? 0 : billingCycle switch
            {
                BillingCycle.SixMonths => plan.PriceSixMonths,
                BillingCycle.Yearly => plan.PriceYearly,
                _ => plan.PriceMonthly
            };

            string paymentRedirectUrl = null;
            string trackingNumber = null;
            int? subscriptionOrderId = null;

            if (plan != null)
            {
                var orderId = await _tenantPlanService.CreateSubscriptionOrderAsync(
                    provisioningResult.StoreId, provisioningResult.OwnerCustomerId, dto.PlanId, billingCycle);

                // ۰ یعنی پلن آزمایشی بود — هیچ سفارش/پرداختی لازم نیست (طبق طراحی CreateSubscriptionOrderAsync).
                if (orderId > 0)
                {
                    subscriptionOrderId = orderId;

                    if (planCost > 0)
                    {
                        var paymentResult = await _paymentService.RequestPaymentAsync(
                            TenantPlanService.MasterPlatformStoreId, orderId, planCost,
                            dto.GatewayName ?? "zarinpal", dto.PaymentCallbackUrl);

                        if (paymentResult.IsSuccess)
                        {
                            paymentRedirectUrl = paymentResult.RedirectUrl;
                            // برگرداندن trackingNumber به کلاینت تا مجبور نباشیم حدس بزنیم درگاه چه
                            // پارامترهایی را در URL بازگشتی اضافه می‌کند — کلاینت این مقدار را قبل از
                            // رفتن به درگاه در localStorage خودش نگه می‌دارد و بعد از بازگشت می‌خواند.
                            trackingNumber = paymentResult.TrackingNumber;
                        }
                    }
                }
            }

            // ورود خودکار فروشنده بلافاصله بعد از ثبت‌نام (بدون این‌که لازم باشد جدا لاگین کند).
            var accessToken = _jwtTokenService.GenerateAccessToken(provisioningResult.OwnerCustomerId, provisioningResult.StoreId);

            return Ok(new
            {
                success = true,
                storeId = provisioningResult.StoreId,
                storeUrl = provisioningResult.StoreUrl,
                subscriptionOrderId,
                requiresPayment = planCost > 0,
                paymentRedirectUrl,
                trackingNumber,
                accessToken
            });
        }

        /// <summary>
        /// تایید واقعی پرداخت اشتراک ثبت‌نام — حلقهٔ ناقص قبلی: Signup لینک درگاه برمی‌گرداند ولی
        /// هیچ Endpointی برای Verify بازگشت از درگاه وجود نداشت (فقط به کد Next.js بیرون ریپو
        /// موکول شده بود که چیزی برای صدا زدن نداشت). حالا: تایید واقعی از Parbad → علامت‌گذاری
        /// سفارش به‌عنوان پرداخت‌شده (که OrderPaidEvent را هم فعال می‌کند) → فعال‌سازی اشتراک و
        /// دسترسی فروشگاه. سایت Next.js باید بعد از بازگشت کاربر از درگاه، همین Endpoint را با
        /// (orderId, storeId, trackingNumber, amountToman) صدا بزند.
        /// </summary>
        [HttpPost("payment/verify")]
        public async Task<IActionResult> VerifySignupPayment([FromBody] TenantSignupPaymentVerifyDto dto)
        {
            if (dto == null || dto.OrderId <= 0 || string.IsNullOrWhiteSpace(dto.TrackingNumber) || dto.AmountToman <= 0)
                return BadRequest(new { success = false, message = "اطلاعات تایید پرداخت ناقص است." });

            var order = await _orderService.GetOrderByIdAsync(dto.OrderId);
            if (order == null)
                return NotFound(new { success = false, message = "سفارش اشتراک یافت نشد." });

            var verifyResult = await _paymentService.VerifyPaymentAsync(
                TenantPlanService.MasterPlatformStoreId, dto.TrackingNumber, dto.AmountToman);

            if (!verifyResult.IsSuccess)
                return BadRequest(new { success = false, message = verifyResult.Message });

            // فقط در صورتی سفارش پرداخت‌شده علامت می‌خورد که قبلاً پرداخت نشده باشد
            // (MarkOrderAsPaidAsync رویداد OrderPaidEvent را فعال می‌کند و Consumer آن، مسیر
            // فعال‌سازی را هم انجام می‌دهد — اینجا Idempotent است).
            if (order.PaymentStatusId != (int)PaymentStatus.Paid)
                await _orderProcessingService.MarkOrderAsPaidAsync(order);

            var subscriptionActivated = false;
            if (dto.StoreId > 0)
                subscriptionActivated = await _tenantPlanService.ActivateSubscriptionAsync(dto.StoreId);

            return Ok(new
            {
                success = true,
                alreadyProcessed = verifyResult.AlreadyVerifiedBefore,
                subscriptionActivated,
                message = "پرداخت با موفقیت تایید شد و اشتراک فروشگاه فعال گردید."
            });
        }

        /// <summary>
        /// استعلام فوری آنلاین بودن و امکان رزرو زیردامنه قبل از ثبت‌نام
        /// </summary>
        [HttpGet("check-subdomain")]
        public async Task<IActionResult> CheckSubdomain([FromQuery] string subdomain)
        {
            if (string.IsNullOrWhiteSpace(subdomain))
                return BadRequest(new { isAvailable = false, message = "زیردامنه وارد نشده است." });

            var isAvailable = await _tenantProvisioningService.ValidateSubdomainAvailabilityAsync(subdomain);
            return Ok(new
            {
                Subdomain = subdomain.Trim().ToLowerInvariant(),
                FullDomain = $"{subdomain.Trim().ToLowerInvariant()}.market.com",
                IsAvailable = isAvailable,
                Message = isAvailable ? "این زیردامنه آزاد است و آماده رزرو می‌باشد." : "این زیردامنه قبلاً رزرو شده است."
            });
        }

        /// <summary>
        /// بلوک‌های محتوایی صفحهٔ اصلی (فروشگاه/اپلیکیشن/دستیار اینستاگرام) — کاملاً از پنل مدیریت
        /// قابل ویرایش، طبق درخواست کاربر.
        /// </summary>
        [HttpGet("feature-blocks")]
        public async Task<IActionResult> GetFeatureBlocks()
        {
            var blocks = await _contentBlockService.GetActiveBlocksAsync();
            return Ok(new { blocks = blocks.Select(MapBlockSummary) });
        }

        /// <summary>محتوای کامل صفحهٔ «ادامه مطلب» یک بخش خاص (فروشگاه/اپلیکیشن/دستیار اینستاگرام).</summary>
        [HttpGet("feature-blocks/{pageKey}")]
        public async Task<IActionResult> GetFeatureBlockDetail(string pageKey)
        {
            var block = await _contentBlockService.GetByPageKeyAsync(pageKey);
            if (block == null || !block.IsActive)
                return NotFound(new { success = false, message = "این صفحه یافت نشد." });

            return Ok(new
            {
                pageKey = block.PageKey,
                menuTitle = block.MenuTitle,
                title = block.Title,
                featureBullets = SplitLines(block.FeatureBulletsText),
                fullContentHtml = block.DetailFullContent,
                imageUrls = SplitLines(block.DetailImageUrlsText)
            });
        }

        private static object MapBlockSummary(Domain.LandingContentBlock block) => new
        {
            pageKey = block.PageKey,
            menuTitle = block.MenuTitle,
            title = block.Title,
            summaryText = block.SummaryText,
            featureBullets = SplitLines(block.FeatureBulletsText),
            imageUrl = block.ImageUrl,
            ctaText = block.CtaText
        };

        private static string[] SplitLines(string text) =>
            string.IsNullOrWhiteSpace(text)
                ? System.Array.Empty<string>()
                : text.Replace("\r\n", "\n").Split('\n', System.StringSplitOptions.RemoveEmptyEntries);

        private static BillingCycle ParseBillingCycle(string value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "sixmonths" or "six_months" or "6months" => BillingCycle.SixMonths,
                "yearly" or "annual" => BillingCycle.Yearly,
                _ => BillingCycle.Monthly
            };
        }
    }

    public class TenantSignupRequestDto
    {
        public string StoreName { get; set; }
        public string Subdomain { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string Phone { get; set; }
        public string CompanyName { get; set; }
        public int PlanId { get; set; }

        /// <summary>"monthly" | "sixmonths" | "yearly"</summary>
        public string BillingCycle { get; set; }
        public string GatewayName { get; set; }
        public string PaymentCallbackUrl { get; set; }
    }

    public class TenantSignupPaymentVerifyDto
    {
        public int OrderId { get; set; }

        /// <summary>شناسهٔ فروشگاه ساخته‌شده در Signup — برای فعال‌سازی اشتراک آن.</summary>
        public int StoreId { get; set; }

        public string TrackingNumber { get; set; }
        public decimal AmountToman { get; set; }
    }
}