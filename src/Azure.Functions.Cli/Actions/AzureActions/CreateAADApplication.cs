using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Fclp;
namespace Azure.Functions.Cli.Actions.AzureActions
{
    // Invoke via `func azure auth create-aad -aad-name {displayNameOfAAD} --app-name {displayNameOfApp}`
    [Action(Name = "create-aad", Context = Context.Azure, SubContext = Context.Auth, HelpText = "Creates a production Azure Active Directory application with given name. Links it to specified Azure Application")]
    class CreateAADApplication : BaseAzureAction
    {
        private readonly IAuthManager _authManager;

        public string AADName { get; set; }

        public string AppName { get; set; }

        public CreateAADApplication(IAuthManager authManager)
        {
            _authManager = authManager;
        }

        public override async Task RunAsync()
        {
            await _authManager.CreateAADApplication(AccessToken, AADName, AppName);
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>("aad-name")
                .WithDescription("Name of AD application to create")
                .Callback(f => AADName = f);

            Parser
                .Setup<string>("app-name")
                .WithDescription("Name of Azure Websites Application/Function to link AAD application to")
                .Callback(f => AppName = f);

            return base.ParseArgs(args);
        }
    }
}