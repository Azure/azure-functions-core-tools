using System;
using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Interfaces;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    [Action(Name = "list", Context = Context.Azure, SubContext = Context.Storage, HelpText = "List all Storage Accounts in the selected Azure subscription")]
    class ListStorageAction : BaseAction
    {
        private readonly IArmManager _armManager;
        private readonly ISettings _settings;

        public ListStorageAction(IArmManager armManager, ISettings settings)
        {
            _armManager = armManager;
            _settings = settings;
        }

        public override async Task RunAsync()
        {
            var storageAccounts = await _armManager.GetStorageAccountsAsync(await _armManager.GetCurrentSubscriptionAsync());
            if (storageAccounts.Any())
            {
                ColoredConsole.WriteLine(TitleColor("Storage Accounts:"));

                foreach (var storageAccount in storageAccounts)
                {
                    ColoredConsole
                        .WriteLine($"   -> {TitleColor("Name")}: {storageAccount.StorageAccountName} ({AdditionalInfoColor(storageAccount.Location)})")
                        .WriteLine();
                }
            }
            else
            {
                ColoredConsole.Error.WriteLine(ErrorColor("   -> No storage accounts found"));
            }
        }
    }
}
