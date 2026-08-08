namespace Nop.Plugin.Misc.MultiTenantStores.Controllers.Admin
{
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Nop.Core;
    using Nop.Data;
    using Nop.Plugin.Misc.MultiTenantStores.Domain;
    using Nop.Web.Framework;
    using Nop.Web.Framework.Controllers;
    using Nop.Web.Framework.Mvc.Filters;
    using Nop.Plugin.Misc.MultiTenantStores.Infrastructure.Filters;

    /// <summary>
    /// پنل مدیریت اعتبارنامه‌های BNPL (دیجی‌پی/اسنپ‌پی) به‌ازای هر فروشگاه.
    /// </summary>
    [Area(AreaNames.ADMIN)]
    [AuthorizeAdmin]
    [AutoValidateAntiforgeryToken]
    [ServiceFilter(typeof(TenantAdminScopeFilter))]
    public class BnplAdminController : BasePluginController
    {
        private readonly IRepository<BnplCredential> _credentialRepository;
        private readonly IStoreContext _storeContext;

        public BnplAdminController(IRepository<BnplCredential> credentialRepository, IStoreContext storeContext)
        {
            _credentialRepository = credentialRepository;
            _storeContext = storeContext;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var store = await _storeContext.GetCurrentStoreAsync();
            var creds = await _credentialRepository.GetAllAsync(q => q.Where(c => c.StoreId == store.Id));

            return View("~/Plugins/Misc.MultiTenantStores/Views/BnplAdmin/Index.cshtml", creds);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var store = await _storeContext.GetCurrentStoreAsync();
            var cred = await _credentialRepository.GetByIdAsync(id);
            if (cred == null || cred.StoreId != store.Id)
                return NotFound();

            return View("~/Plugins/Misc.MultiTenantStores/Views/BnplAdmin/Edit.cshtml", cred);
        }

        [HttpPost]
        public async Task<IActionResult> Save(BnplCredential model)
        {
            var store = await _storeContext.GetCurrentStoreAsync();

            if (model.Id > 0)
            {
                var existing = await _credentialRepository.GetByIdAsync(model.Id);
                if (existing == null || existing.StoreId != store.Id)
                    return NotFound();

                existing.Username = model.Username;
                existing.Password = model.Password;
                existing.ClientId = model.ClientId;
                existing.ClientSecret = model.ClientSecret;
                existing.Environment = model.Environment;
                existing.BaseUrlOverride = model.BaseUrlOverride;
                existing.IsActive = model.IsActive;
                existing.UpdatedOnUtc = System.DateTime.UtcNow;

                await _credentialRepository.UpdateAsync(existing);
            }
            else
            {
                await _credentialRepository.InsertAsync(new BnplCredential
                {
                    StoreId = store.Id,
                    ProviderKey = model.ProviderKey?.Trim().ToLowerInvariant(),
                    Username = model.Username,
                    Password = model.Password,
                    ClientId = model.ClientId,
                    ClientSecret = model.ClientSecret,
                    Environment = model.Environment,
                    BaseUrlOverride = model.BaseUrlOverride,
                    IsActive = model.IsActive,
                    CreatedOnUtc = System.DateTime.UtcNow,
                    UpdatedOnUtc = System.DateTime.UtcNow
                });
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var store = await _storeContext.GetCurrentStoreAsync();
            var cred = await _credentialRepository.GetByIdAsync(id);
            if (cred == null || cred.StoreId != store.Id)
                return NotFound();

            await _credentialRepository.DeleteAsync(cred);
            return RedirectToAction("Index");
        }
    }
}
