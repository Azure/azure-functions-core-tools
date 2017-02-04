using System;
using System.Linq;
using System.Threading.Tasks;
using Fclp;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "delete", Context = Context.Settings, HelpText = "Remove a local setting")]
    [Action(Name = "remove", Context = Context.Settings, HelpText = "Remove a local setting")]
    class DeleteSettingAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;
        public string Name { get; set; }
        public bool IsConnectionString { get; set; }

        public DeleteSettingAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<bool>("connectionString")
                .SetDefault(false)
                .Callback(f => IsConnectionString = f)
                .WithDescription("Specifying this removes the value from the connection strings collection instead.");

            if (args.Length == 0)
            {
                throw new CliArgumentsException("Must specify setting name.", Parser.Parse(args),
                    new CliArgument { Name = nameof(Name), Description = "Name of app setting to be deleted." });
            }
            else
            {
                Name = args.First();
                return base.ParseArgs(args);
            }
        }

        public override Task RunAsync()
        {
            _secretsManager.DeleteSecret(Name);
            return Task.CompletedTask;
        }
    }
}
