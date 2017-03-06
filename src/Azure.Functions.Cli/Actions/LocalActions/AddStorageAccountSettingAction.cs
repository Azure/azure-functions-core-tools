using System;
using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using Fclp;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Interfaces;
using static Azure.Functions.Cli.Common.OutputTheme;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "add-storage-account", Context = Context.Settings, HelpText = "Add a local app setting using the value from an Azure Storage account. Requires Azure login.")]
    internal class AddStorageAccountSettingAction : BaseAction
    {
        private readonly IArmManager _armManager;
        private readonly ISettings _settings;
        private readonly ISecretsManager _secretsManager;

        public string StorageAccountName { get; set; }

        public AddStorageAccountSettingAction(IArmManager armManager, ISettings settings, ISecretsManager secretsManager)
        {
            _armManager = armManager;
            _settings = settings;
            _secretsManager = secretsManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            if (args.Any())
            {
                StorageAccountName = args.First();
            }
            else
            {
                throw new CliArgumentsException("Must specify storage account name.",
                    new CliArgument { Name = nameof(StorageAccountName), Description = "Storage Account Name" });
            }

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            var storageAccounts = await _armManager.GetStorageAccountsAsync();
            var storageAccount = storageAccounts.FirstOrDefault(st => st.StorageAccountName.Equals(StorageAccountName, StringComparison.OrdinalIgnoreCase));

            if (storageAccount == null)
            {
                ColoredConsole
                    .Error
                    .WriteLine(ErrorColor($"Can't find storage account with name {StorageAccountName} in current subscription ({_settings.CurrentSubscription})"));
            }
            else
            {
                storageAccount = await _armManager.LoadAsync(storageAccount);
                var name = $"{storageAccount.StorageAccountName}_STORAGE";
                _secretsManager.SetSecret(name, storageAccount.GetConnectionString());
                ColoredConsole
                    .WriteLine($"Secret saved locally in {ExampleColor(name)}")
                    .WriteLine();
            }
        }
    }
}
