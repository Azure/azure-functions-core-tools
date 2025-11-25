// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "add", Context = Context.Settings, HelpText = "Add new local app setting to local.settings.json. Settings are encrypted by default. If encrypted, they can only be decrypted on the current machine.")]
    internal class AddSettingAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;

        public AddSettingAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public string Name { get; set; }

        public string Value { get; set; }

        public bool IsConnectionString { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<bool>("connectionString")
                .SetDefault(false)
                .Callback(f => IsConnectionString = f)
                .WithDescription("Specifying this adds the name-value pair to connection strings collection instead.");

            if (args.Length == 0 && !ScriptHostHelpers.IsHelpRunning)
            {
                throw new CliArgumentsException(
                    "Must specify setting name.",
                    base.ParseArgs(args),
                    new CliArgument { Name = nameof(Name), Description = "App setting name" },
                    new CliArgument { Name = nameof(Value), Description = "(Optional) App setting value. Omit for secure values." });
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
