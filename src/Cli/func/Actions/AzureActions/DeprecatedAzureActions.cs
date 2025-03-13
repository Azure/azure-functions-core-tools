using System.Threading.Tasks;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    [Action(Name = "enable-git-repo", Context = Context.Azure, SubContext = Context.FunctionApp, ShowInHelp = false)]
    [Action(Name = "get-publish-username", Context = Context.Azure, ShowInHelp = false)]
    [Action(Name = "list", Context = Context.Azure, SubContext = Context.Account, ShowInHelp = false)]
    [Action(Name = "list", Context = Context.Azure, SubContext = Context.Subscriptions, ShowInHelp = false)]
    [Action(Name = "list", Context = Context.Azure, SubContext = Context.FunctionApp, ShowInHelp = false)]
    [Action(Name = "list", Context = Context.Azure, SubContext = Context.Storage, ShowInHelp = false)]
    [Action(Name = "login", Context = Context.Azure, ShowInHelp = false)]
    [Action(Name = "logout", Context = Context.Azure, ShowInHelp = false)]
    [Action(Name = "set", Context = Context.Azure, SubContext = Context.Account, ShowInHelp = false)]
    [Action(Name = "set", Context = Context.Azure, SubContext = Context.Subscriptions, ShowInHelp = false)]
    [Action(Name = "set-publish-password", Context = Context.Azure, ShowInHelp = false)]
    [Action(Name = "set-publish-username", Context = Context.Azure, ShowInHelp = false)]
    internal class DeprecatedAzureActions : BaseAction
    {
        public override Task RunAsync()
        {
            throw new CliException("This command has been removed. Please use az-cli (https://docs.microsoft.com/en-us/cli/azure/install-azure-cli) or Azure Powershell (https://docs.microsoft.com/en-us/powershell/azure/overview) for management commands.");
        }
    }
}