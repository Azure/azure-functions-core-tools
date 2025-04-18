// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    [Action(Name = "fetch-app-settings", Context = Context.Azure, SubContext = Context.FunctionApp, HelpText = "Retrieve App Settings from your Azure-hosted Function App and store locally")]
    [Action(Name = "fetch", Context = Context.Azure, SubContext = Context.FunctionApp, HelpText = "Retrieve App Settings from your Azure-hosted Function App and store locally")]
    internal class FetchAppSettingsAction : BaseFunctionAppAction
    {
        private readonly ISecretsManager _secretsManager;

        public FetchAppSettingsAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public override async Task RunAsync()
        {
            var functionApp = await AzureHelper.GetFunctionApp(FunctionAppName, AccessToken, ManagementURL, Slot, Subscription);
            if (functionApp != null)
            {
                ColoredConsole.WriteLine(TitleColor("App Settings:"));
                foreach (var pair in functionApp.AzureAppSettings)
                {
                    ColoredConsole.WriteLine($"Loading {pair.Key} = *****");
                    _secretsManager.SetSecret(pair.Key, pair.Value);
                }

                ColoredConsole.WriteLine();

                ColoredConsole.WriteLine(TitleColor("Connection Strings:"));
                foreach (var connectionString in functionApp.ConnectionStrings)
                {
                    ColoredConsole.WriteLine($"Loading {connectionString.Key} = *****");
                    _secretsManager.SetConnectionString(connectionString.Key, connectionString.Value.Value);
                }
            }
            else
            {
                ColoredConsole.Error.WriteLine(ErrorColor($"Can't find function app by name {FunctionAppName}"));
            }
        }
    }
}
