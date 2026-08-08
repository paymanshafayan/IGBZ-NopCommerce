namespace Nop.Plugin.Misc.MasterSiteHub.Components
{
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Core.Domain.Catalog;
    using Nop.Services.Catalog;
    using Nop.Services.Media;
    using Nop.Web.Framework.Components;

    /// <summary>
    /// نوار استوری + Grid محصولات به‌سبک اینستاگرام — داده‌ها واقعی هستند (محصولات واقعی همان
    /// فروشگاه)، نه نمونهٔ ساختگی. نوار استوری از روی محصولات تازه‌منتشرشده ساخته می‌شود چون
    /// هیچ موجودیت واقعی «Story» در دیتابیس این پلتفرم ذخیره نمی‌شود (استوری واقعی اینستاگرام
    /// مستقیماً در خودِ اینستاگرام است، نه این پلتفرم).
    /// </summary>
    public class InstagramGridViewComponent : NopViewComponent
    {
        private const int GridProductCount = 12;
        private const int StoriesCount = 8;

        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly IProductService _productService;
        private readonly IPictureService _pictureService;
        private readonly Nop.Services.Seo.IUrlRecordService _urlRecordService;

        public InstagramGridViewComponent(
            IWorkContext workContext,
            IStoreContext storeContext,
            IProductService productService,
            IPictureService pictureService,
            Nop.Services.Seo.IUrlRecordService urlRecordService)
        {
            _workContext = workContext;
            _storeContext = storeContext;
            _productService = productService;
            _pictureService = pictureService;
            _urlRecordService = urlRecordService;
        }

        public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
        {
            var store = await _storeContext.GetCurrentStoreAsync();

            var products = await _productService.SearchProductsAsync(
                storeId: store.Id,
                visibleIndividuallyOnly: true,
                orderBy: ProductSortingEnum.CreatedOn,
                pageSize: GridProductCount);

            var tiles = new System.Collections.Generic.List<InstagramGridTileModel>();
            foreach (var product in products)
            {
                var pictures = await _pictureService.GetPicturesByProductIdAsync(product.Id, 1);
                var firstPicture = pictures.FirstOrDefault();

                string imageUrl;
                if (firstPicture != null)
                {
                    var picResult = await _pictureService.GetPictureUrlAsync(firstPicture, 600);
                    imageUrl = picResult.Url;
                }
                else
                {
                    imageUrl = await _pictureService.GetDefaultPictureUrlAsync(600);
                }

                var seName = await _urlRecordService.GetSeNameAsync(product);

                tiles.Add(new InstagramGridTileModel
                {
                    ProductId = product.Id,
                    Name = product.Name,
                    ImageUrl = imageUrl,
                    Price = product.Price,
                    Sku = product.Sku,
                    DetailUrl = string.IsNullOrEmpty(seName) ? $"/product/{product.Id}" : $"/{seName}"
                });
            }

            var model = new InstagramGridModel
            {
                Tiles = tiles,
                // نوار استوری همان محصولات تازه هستند، فقط با نمایش دایره‌ای؛ محدود به تعداد کمتر.
                Stories = tiles.Take(StoriesCount).ToList()
            };

            return View("~/Plugins/Misc.MasterSiteHub/Views/Shared/Components/InstagramGrid/Default.cshtml", model);
        }
    }

    public class InstagramGridModel
    {
        public System.Collections.Generic.List<InstagramGridTileModel> Tiles { get; set; }
        public System.Collections.Generic.List<InstagramGridTileModel> Stories { get; set; }
    }

    public class InstagramGridTileModel
    {
        public int ProductId { get; set; }
        public string Name { get; set; }
        public string ImageUrl { get; set; }
        public decimal Price { get; set; }
        public string Sku { get; set; }
        public string DetailUrl { get; set; }
    }
}
