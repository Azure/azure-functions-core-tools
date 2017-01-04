using System;
using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    [Action(Name = "login", Context = Context.Azure, HelpText = "Log in to an Azure account")]
    class LoginAction : BaseAzureAccountAction
    {
        public LoginAction(IArmManager armManager, ISettings settings)
            : base(armManager, settings)
        {
        }

        public override async Task RunAsync()
        {
            await ArmManager.LoginAsync();
            await PrintAccountsAsync();
        }
    }
}
