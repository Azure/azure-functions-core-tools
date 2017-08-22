using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Arm;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    [Action(Name = "logout", Context = Context.Azure, HelpText = "Log out of Azure account")]
    class LogoutAction : BaseAction
    {
        private readonly IArmTokenManager _tokenManager;

        public LogoutAction(IArmTokenManager tokenManager)
        {
            _tokenManager = tokenManager;
        }

        public override Task RunAsync()
        {
            _tokenManager.Logout();
            return Task.CompletedTask;
        }
    }
}
