using System;
using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using Fclp;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "add", Context = Context.Settings, HelpText = "Add new local app setting to local.settings.json. Settings are encrypted by default. If encrypted, they can only be decrypted on the current machine.")]
    class AddSettingAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;

        public string Name { get; set; }
        public string Value { get; set; }
        public bool IsConnectionString { get; set; }

        public AddSettingAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<bool>("connectionString")
                .SetDefault(false)
                .Callback(f => IsConnectionString = f)
                .WithDescription("Specifying this adds the name-value pair to connection strings collection instead.");

            if (args.Length == 0)
            {
                throw new CliArgumentsException("Must specify setting name.", Parser.Parse(args),
                    new CliArgument { Name = nameof(Name), Description = "App setting name" },
                    new CliArgument { Name = nameof(Value), Description = "(Optional) App setting value. Omit for secure values."});
            }
            else
            {
                Name = args.FirstOrDefault();
                Value = args.Skip(1).FirstOrDefault();
                return base.ParseArgs(args);
            }
        }

        public override Task RunAsync()
        {
            if (string.IsNullOrEmpty(Value))
            {
                ColoredConsole.Write("Please enter the value: ");
                Value = SecurityHelpers.ReadPassword();
            }
            if (IsConnectionString)
            {
                _secretsManager.SetConnectionString(Name, Value);
            }
            else
            {
                _secretsManager.SetSecret(Name, Value);
            }
            return Task.CompletedTask;
        }
    }
}
