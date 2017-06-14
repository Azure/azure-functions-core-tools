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
            : base(armManager, settings)
        { }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            if (args.Length > 0)
                _username = args[0];
            if (args.Length > 1)
                _password = args[1];
            
            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            if (string.IsNullOrWhiteSpace(_username))
                await _armManager.LoginAsync();
            else
                await _armManager.LoginAsync(_username, _password);

            await PrintAccountsAsync();
        }
    }
}
