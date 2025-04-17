// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    [Action(Name = "fetch-connection-string", Context = Context.Azure, SubContext = Context.Storage, HelpText = "Add a local app setting using the value from an Azure Storage account. Requires Azure login.")]
    internal class AddStorageAccountSettingAction : BaseAzureAction
    {
        private readonly ISettings _settings;
        private readonly ISecretsManager _secretsManager;

        public AddStorageAccountSettingAction(ISettings settings, ISecretsManager secretsManager)
        {
            _settings = settings;
            _secretsManager = secretsManager;
        }

        public string StorageAccountName { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            if (args.Any())
            {
                StorageAccountName = args.First();
            }
            else
            {
                throw new CliArgumentsException(
                    "Must specify storage account name.",
                    new CliArgument { Name = nameof(StorageAccountName), Description = "Storage Account Name" });
            }

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            var storageAccount = await AzureHelper.GetStorageAccount(StorageAccountName, AccessToken, ManagementURL);

            var name = $"{storageAccount.StorageAccountName}_STORAGE";
            _secretsManager.SetSecret(name, storageAccount.ConnectionString);
            ColoredConsole
                .WriteLine($"Secret saved locally in {SecretsManager.AppSettingsFileName} under name {ExampleColor(name)}.")
                .WriteLine();
        }
    }
}
