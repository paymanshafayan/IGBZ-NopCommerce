namespace Nop.Plugin.Misc.MultiTenantStores.Controllers.Admin
{
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Rendering;
    using Nop.Core;
    using Nop.Services.Security;
    using Nop.Web.Framework;
    using Nop.Web.Framework.Controllers;
    using Nop.Web.Framework.Mvc.Filters;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;
    using Nop.Plugin.Misc.MultiTenantStores.Models;
    using Nop.Plugin.Misc.MultiTenantStores.Services;

    /// <summary>
    /// پنل مدیریت اعتبارنامه‌های سرویس‌های بیرونی (بخش ۱۰ سند معماری). هر ادمین تننت فقط
    /// اعتبارنامه‌های فروشگاه خودش را می‌بیند/ویرایش می‌کند (از طریق IStoreContext که با
    /// MultiTenantStoreContext جایگزین شده است).
    /// </summary>
    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [AutoValidateAntiforgeryToken]
    [ServiceFilter(typeof(Infrastructure.Filters.TenantAdminScopeFilter))]
    public class IntegrationCredentialsController : BasePluginController
    {
        private readonly IStoreContext _storeContext;
        private readonly IPermissionService _permissionService;
        private readonly ITenantIntegrationCredentialService _credentialService;

        public IntegrationCredentialsController(
            IStoreContext storeContext,
            IPermissionService permissionService,
            ITenantIntegrationCredentialService credentialService)
        {
            _storeContext = storeContext;
            _permissionService = permissionService;
            _credentialService = credentialService;
        }

        private async Task<bool> HasAccessAsync() =>
            await _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS);

        public async Task<IActionResult> Index()
        {
            if (!await HasAccessAsync())
                return AccessDeniedView();

            var currentStore = await _storeContext.GetCurrentStoreAsync();
            var entities = await _credentialService.GetByStoreIdAsync(currentStore.Id);

            var model = new IntegrationCredentialListModel { StoreId = currentStore.Id };
            foreach (var entity in entities)
            {
                model.Credentials.Add(MapToModel(entity));
            }

            return View("~/Plugins/Misc.MultiTenantStores/Views/IntegrationCredentials/Index.cshtml", model);
        }

        public async Task<IActionResult> Create()
        {
            if (!await HasAccessAsync())
                return AccessDeniedView();

            var currentStore = await _storeContext.GetCurrentStoreAsync();
            var model = new IntegrationCredentialModel { StoreId = currentStore.Id, IsActive = true };
            PopulateProviderKeys(model);

            return View("~/Plugins/Misc.MultiTenantStores/Views/IntegrationCredentials/CreateOrUpdate.cshtml", model);
        }

        [HttpPost]
        public async Task<IActionResult> Create(IntegrationCredentialModel model)
        {
            if (!await HasAccessAsync())
                return AccessDeniedView();

            var currentStore = await _storeContext.GetCurrentStoreAsync();

            if (!ModelState.IsValid)
            {
                PopulateProviderKeys(model);
                return View("~/Plugins/Misc.MultiTenantStores/Views/IntegrationCredentials/CreateOrUpdate.cshtml", model);
            }

            await _credentialService.SaveAsync(null, currentStore.Id, model.ProviderKey,
                model.ApiKeyMaskedOrNew, model.ApiSecretMaskedOrNew, model.EndpointOverrideUrl, model.IsActive);

            return RedirectToAction("Index");
        }

        public async Task<IActionResult> Edit(int id)
        {
            if (!await HasAccessAsync())
                return AccessDeniedView();

            var entity = await _credentialService.GetByIdAsync(id);
            var currentStore = await _storeContext.GetCurrentStoreAsync();
            if (entity == null || entity.StoreId != currentStore.Id)
                return RedirectToAction("Index");

            var model = MapToModel(entity);
            PopulateProviderKeys(model);

            return View("~/Plugins/Misc.MultiTenantStores/Views/IntegrationCredentials/CreateOrUpdate.cshtml", model);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(IntegrationCredentialModel model)
        {
            if (!await HasAccessAsync())
                return AccessDeniedView();

            var currentStore = await _storeContext.GetCurrentStoreAsync();

            if (!ModelState.IsValid)
            {
                PopulateProviderKeys(model);
                return View("~/Plugins/Misc.MultiTenantStores/Views/IntegrationCredentials/CreateOrUpdate.cshtml", model);
            }

            await _credentialService.SaveAsync(model.Id, currentStore.Id, model.ProviderKey,
                model.ApiKeyMaskedOrNew, model.ApiSecretMaskedOrNew, model.EndpointOverrideUrl, model.IsActive);

            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            if (!await HasAccessAsync())
                return AccessDeniedView();

            var entity = await _credentialService.GetByIdAsync(id);
            var currentStore = await _storeContext.GetCurrentStoreAsync();
            if (entity != null && entity.StoreId == currentStore.Id)
                await _credentialService.DeleteAsync(id);

            return RedirectToAction("Index");
        }

        /// <summary>
        /// این اکشن هرگز به‌سادگی «موفق» برنمی‌گرداند — نتیجهٔ واقعی فراخوانی شبکه‌ای را نمایش
        /// می‌دهد، شامل این هشدار صریح که فقط در دسترس بودن سرور را می‌سنجد، نه صحت کلید API.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> TestConnection(int id)
        {
            if (!await HasAccessAsync())
                return Json(new { success = false, message = "دسترسی رد شد." });

            var entity = await _credentialService.GetByIdAsync(id);
            var currentStore = await _storeContext.GetCurrentStoreAsync();
            if (entity == null || entity.StoreId != currentStore.Id)
                return Json(new { success = false, message = "رکورد یافت نشد." });

            var result = await _credentialService.TestConnectionAsync(id);
            return Json(new { success = result.Success, message = result.Message });
        }

        private IntegrationCredentialModel MapToModel(TenantIntegrationCredential entity)
        {
            var metadata = _credentialService.GetProviderMetadata(entity.ProviderKey);
            return new IntegrationCredentialModel
            {
                Id = entity.Id,
                StoreId = entity.StoreId,
                ProviderKey = entity.ProviderKey,
                ProviderGuideUrl = metadata.GuideUrl,
                ApiKeyMaskedOrNew = _credentialService.DecryptForDisplayMasked(entity.ApiKey),
                ApiSecretMaskedOrNew = _credentialService.DecryptForDisplayMasked(entity.ApiSecret),
                EndpointOverrideUrl = entity.EndpointOverrideUrl,
                IsActive = entity.IsActive,
                IsVerified = entity.IsVerified,
                LastTestedOnUtc = entity.LastTestedOnUtc,
                LastTestResultMessage = entity.LastTestResultMessage
            };
        }

        private void PopulateProviderKeys(IntegrationCredentialModel model)
        {
            model.AvailableProviderKeys = _credentialService.GetKnownProviderKeys()
                .Select(k =>
                {
                    var meta = _credentialService.GetProviderMetadata(k);
                    return new SelectListItem { Text = $"{meta.DisplayName} ({k})", Value = k, Selected = k == model.ProviderKey };
                })
                .ToList();

            if (!string.IsNullOrEmpty(model.ProviderKey))
                model.ProviderGuideUrl = _credentialService.GetProviderMetadata(model.ProviderKey).GuideUrl;
        }
    }
}
