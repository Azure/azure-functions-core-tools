using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    [Action(Name = "portal", Context = Context.Azure, HelpText = "Launch default browser with link to the current app in https://portal.azure.com")]
    class PortalAction : BaseFunctionAppAction
    {
        private readonly ISettings _settings;
        public PortalAction(IArmManager armManager, ISettings settings)
            : base(armManager)
        {
            _settings = settings;
        }

        public override async Task RunAsync()
        {
            var functionApp = await _armManager.GetFunctionAppAsync(FunctionAppName);
            var currentTenant = _settings.CurrentTenant;
            var portalHostName = "https://portal.azure.com";
            Process.Start($"{portalHostName}/{currentTenant}#resource{functionApp.ArmId}");
        }
    }
}
