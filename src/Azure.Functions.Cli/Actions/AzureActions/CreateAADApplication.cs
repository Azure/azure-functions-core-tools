using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    // Invoke via `func azure auth create-aad-app --AADAppRegistrationName {yourAppRegistrationName} --appName {yourAppName}`
    [Action(Name = "create-aad-app", Context = Context.Azure, SubContext = Context.Auth, HelpText = "Creates an Azure Active Directory app registration. Can be linked to an Azure App Service or Function app.")]
    class CreateAADApplication : BaseAzureAction
    {
        private readonly IAuthManager _authManager;
        private readonly ISecretsManager _secretsManager;

        public string AADAppRegistrationName { get; set; }

        public string AppName { get; set; }

        public CreateAADApplication(IAuthManager authManager, ISecretsManager secretsManager)
        {
            _authManager = authManager;
            _secretsManager = secretsManager;
        }

        public override async Task RunAsync()
        {
            var workerRuntime = WorkerRuntimeLanguageHelper.GetCurrentWorkerRuntimeLanguage(_secretsManager);

            await _authManager.CreateAADApplication(AccessToken, AADAppRegistrationName, workerRuntime, AppName);
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>("AADAppRegistrationName")
                .WithDescription("Name of the Azure Active Directory app registration to create.")
                .Callback(f => AADAppRegistrationName = f);

            Parser
                .Setup<string>("appName")
                .WithDescription("Name of the Azure App Service or Azure Functions app which will be connected to the Azure AD app registration.")
                .Callback(f => AppName = f);

            return base.ParseArgs(args);
        }
    }
}