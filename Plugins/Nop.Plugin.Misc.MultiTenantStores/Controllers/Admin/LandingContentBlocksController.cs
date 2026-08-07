namespace Nop.Plugin.Misc.MultiTenantStores.Controllers.Admin
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Services.Customers;
    using Nop.Services.Common;
    using Nop.Web.Framework;
    using Nop.Web.Framework.Controllers;
    using Nop.Web.Framework.Mvc.Filters;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// مدیریت بلوک‌های محتوایی سایت مادر (فروشگاه/اپلیکیشن/دستیار اینستاگرام) — کاملاً قابل
    /// درج/ویرایش/حذف از پنل مدیریت، طبق درخواست صریح کاربر. فقط سوپرادمین دسترسی دارد.
    /// </summary>
    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [AutoValidateAntiforgeryToken]
    public class LandingContentBlocksController : BasePluginController
    {
        private readonly IWorkContext _workContext;
        private readonly ICustomerService _customerService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly ILandingContentBlockService _blockService;

        public LandingContentBlocksController(
            IWorkContext workContext,
            ICustomerService customerService,
            IGenericAttributeService genericAttributeService,
            ILandingContentBlockService blockService)
        {
            _workContext = workContext;
            _customerService = customerService;
            _genericAttributeService = genericAttributeService;
            _blockService = blockService;
        }

        private async Task<bool> IsSuperAdminAsync()
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            if (customer == null) return false;
            var isTenantOwner = await _genericAttributeService.GetAttributeAsync<bool>(customer, "IsTenantOwner");
            return await _customerService.IsAdminAsync(customer) && !isTenantOwner;
        }

        public async Task<IActionResult> Index()
        {
            if (!await IsSuperAdminAsync())
                return AccessDeniedView();

            var blocks = await _blockService.GetAllBlocksAsync();
            return View("~/Plugins/Misc.MultiTenantStores/Views/LandingContentBlocks/Index.cshtml", blocks);
        }

        public async Task<IActionResult> Create()
        {
            if (!await IsSuperAdminAsync())
                return AccessDeniedView();

            return View("~/Plugins/Misc.MultiTenantStores/Views/LandingContentBlocks/CreateOrUpdate.cshtml", new LandingContentBlock { IsActive = true });
        }

        [HttpPost]
        public async Task<IActionResult> Create(LandingContentBlock model)
        {
            if (!await IsSuperAdminAsync())
                return AccessDeniedView();

            await _blockService.InsertAsync(model);
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> Edit(int id)
        {
            if (!await IsSuperAdminAsync())
                return AccessDeniedView();

            var block = await _blockService.GetByIdAsync(id);
            if (block == null)
                return RedirectToAction("Index");

            return View("~/Plugins/Misc.MultiTenantStores/Views/LandingContentBlocks/CreateOrUpdate.cshtml", block);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(LandingContentBlock model)
        {
            if (!await IsSuperAdminAsync())
                return AccessDeniedView();

            var block = await _blockService.GetByIdAsync(model.Id);
            if (block == null)
                return RedirectToAction("Index");

            block.PageKey = model.PageKey;
            block.MenuTitle = model.MenuTitle;
            block.Title = model.Title;
            block.SummaryText = model.SummaryText;
            block.FeatureBulletsText = model.FeatureBulletsText;
            block.ImageUrl = model.ImageUrl;
            block.CtaText = model.CtaText;
            block.DetailFullContent = model.DetailFullContent;
            block.DetailImageUrlsText = model.DetailImageUrlsText;
            block.DisplayOrder = model.DisplayOrder;
            block.IsActive = model.IsActive;

            await _blockService.UpdateAsync(block);
            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            if (!await IsSuperAdminAsync())
                return AccessDeniedView();

            await _blockService.DeleteAsync(id);
            return RedirectToAction("Index");
        }
    }
}
