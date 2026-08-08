namespace Nop.Plugin.Misc.InstagramAssistant
{
    using System.Threading.Tasks;
    using Nop.Services.Common;
    using Nop.Services.Plugins;

    public class InstagramAssistantPlugin : BasePlugin, IMiscPlugin
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
            return "/Admin/InstagramAssistant/Configure";
        }
    }
}
