namespace Nop.Plugin.Api
{
    using System.Threading.Tasks;
    using Nop.Services.Common;
    using Nop.Services.Plugins;

    public class ApiPlugin : BasePlugin, IMiscPlugin
    {
        public override async Task InstallAsync()
        {
            await base.InstallAsync();
        }

        public override async Task UninstallAsync()
        {
            await base.UninstallAsync();
        }

        public string GetConfigurationUrl()
        {
            return "/Admin/Api/Configure";
        }
    }
}
