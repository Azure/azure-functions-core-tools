using System;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    [Action(Name = "login", Context = Context.Azure, HelpText = "Log in to an Azure account")]
    class LoginAction : BaseAzureAccountAction
    {
        private string _username = string.Empty;
        private string _password = string.Empty;

        public LoginAction(IArmManager armManager, ISettings settings)
            : base(armManager, settings, requiresLogin: false)
        { }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser.Setup<string>('u')
                .WithDescription("username")
                .Callback(username => _username = username);

            Parser.Setup<string>('w')
                .WithDescription("password")
                .Callback(password => _password = password);
            
            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            if (string.IsNullOrWhiteSpace(_username))
                await _armManager.LoginAsync();
            else
            {
                if (string.IsNullOrWhiteSpace(_password))
                    _password = PromptForPassword();

                await _armManager.LoginAsync(_username, _password);
            }

            await PrintAccountsAsync();
        }

        private static string PromptForPassword()
        {
            Console.Write("password: ");
            return SecurityHelpers.ReadPassword();
        }
    }
}
