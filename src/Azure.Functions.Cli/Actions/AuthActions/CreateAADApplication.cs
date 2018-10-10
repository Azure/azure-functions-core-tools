using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Fclp;
namespace Azure.Functions.Cli.Actions.AuthActions
{
    // Invoke via `func auth create-aad --aad-name {displayNameOfAAD}`
    [Action(Name = "create-aad", Context = Context.Auth, HelpText = "Creates an Azure Active Directory application with given application name for local development")]
    class CreateAADApplication : BaseAuthAction
    {
        private readonly IAuthManager _authManager;

        public string AADName { get; set; }

        public CreateAADApplication(IAuthManager authManager)
        {
            _authManager = authManager;
        }

        public override async Task RunAsync()
        {
            await _authManager.CreateAADApplication(AccessToken, AADName);
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>("aad-name")
                .WithDescription("Name of AD application to create")
                .Callback(f => AADName = f);

            return base.ParseArgs(args);
        }
    }
}