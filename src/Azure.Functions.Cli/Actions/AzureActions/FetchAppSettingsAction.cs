using System;
using System.Threading.Tasks;
using Colors.Net;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Interfaces;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    [Action(Name = "fetch-app-settings", Context = Context.Azure, SubContext = Context.FunctionApp, HelpText = "Retrieve App Settings from your Azure-hosted Function App and store locally")]
    [Action(Name = "fetch", Context = Context.Azure, SubContext = Context.FunctionApp, HelpText = "Retrieve App Settings from your Azure-hosted Function App and store locally")]
    internal class FetchAppSettingsAction : BaseFunctionAppAction
    {
        private ISecretsManager _secretsManager;

        public FetchAppSettingsAction(IArmManager armManager, ISecretsManager secretsManager)
            : base(armManager)
        {
            _secretsManager = secretsManager;
        }

        public override async Task RunAsync()
        {
            var functionApp = await _armManager.GetFunctionAppAsync(FunctionAppName);
            if (functionApp != null)
            {
                ColoredConsole.WriteLine(TitleColor("App Settings:"));
                var appSettings = await _armManager.GetFunctionAppAppSettings(functionApp);
                foreach (var pair in appSettings)
                {
                    ColoredConsole.WriteLine($"Loading {pair.Key} = *****");
                    _secretsManager.SetSecret(pair.Key, pair.Value);
                }

                ColoredConsole.WriteLine();

                ColoredConsole.WriteLine(TitleColor("Connection Strings:"));
                var connectionStrings = await _armManager.GetFunctionAppConnectionStrings(functionApp);
                foreach (var connectionString in connectionStrings)
                {
                    ColoredConsole.WriteLine($"Loading {connectionString.Key} = *****");
                    _secretsManager.SetConnectionString(connectionString.Key, connectionString.Value.Value, connectionString.Value.Type);
                }

            }
            else
            {
                ColoredConsole.Error.WriteLine(ErrorColor($"Can't find function app by name {FunctionAppName}"));
            }
        }
    }
}
