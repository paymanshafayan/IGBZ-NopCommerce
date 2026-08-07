namespace Nop.Plugin.Misc.MultiTenantStores.Models
{
    using System;
    using System.Collections.Generic;
    using Microsoft.AspNetCore.Mvc.Rendering;
    using Nop.Web.Framework.Models;
    using Nop.Web.Framework.Mvc.ModelBinding;

    public class IntegrationCredentialListModel
    {
        public int StoreId { get; set; }
        public IList<IntegrationCredentialModel> Credentials { get; set; } = new List<IntegrationCredentialModel>();
    }

    public partial record IntegrationCredentialModel : BaseNopEntityModel
    {
        public int StoreId { get; set; }

        [NopResourceDisplayName("Plugins.Misc.MultiTenantStores.Credential.ProviderKey")]
        public string ProviderKey { get; set; }

        public IList<SelectListItem> AvailableProviderKeys { get; set; } = new List<SelectListItem>();

        /// <summary>لینک راهنمای دریافت API Key این Provider — برای کمک به کاربر هنگام ثبت اعتبارنامه</summary>
        public string ProviderGuideUrl { get; set; }

        /// <summary>
        /// در فرم Edit، این فیلد فقط نسخهٔ ماسک‌شده (مثلاً "••••••ab12") را برای نمایش نگه می‌دارد؛
        /// اگر کاربر آن را خالی بگذارد و ذخیره کند، مقدار واقعی قبلی دست‌نخورده باقی می‌ماند.
        /// اگر مقدار جدیدی تایپ شود، همان به‌عنوان کلید جدید رمزنگاری و ذخیره می‌شود.
        /// </summary>
        [NopResourceDisplayName("Plugins.Misc.MultiTenantStores.Credential.ApiKey")]
        public string ApiKeyMaskedOrNew { get; set; }

        [NopResourceDisplayName("Plugins.Misc.MultiTenantStores.Credential.ApiSecret")]
        public string ApiSecretMaskedOrNew { get; set; }

        [NopResourceDisplayName("Plugins.Misc.MultiTenantStores.Credential.EndpointOverrideUrl")]
        public string EndpointOverrideUrl { get; set; }

        [NopResourceDisplayName("Plugins.Misc.MultiTenantStores.Credential.IsActive")]
        public bool IsActive { get; set; }

        [NopResourceDisplayName("Plugins.Misc.MultiTenantStores.Credential.IsVerified")]
        public bool IsVerified { get; set; }

        [NopResourceDisplayName("Plugins.Misc.MultiTenantStores.Credential.LastTestedOnUtc")]
        public DateTime? LastTestedOnUtc { get; set; }

        [NopResourceDisplayName("Plugins.Misc.MultiTenantStores.Credential.LastTestResultMessage")]
        public string LastTestResultMessage { get; set; }
    }
}
