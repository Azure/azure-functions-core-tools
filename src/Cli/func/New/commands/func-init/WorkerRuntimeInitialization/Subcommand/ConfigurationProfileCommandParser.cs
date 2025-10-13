// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.Commands.Init
{
    internal class ConfigurationProfileCommandParser : ICommandParser
    {
        public static readonly Option<WorkerRuntime> WorkerRuntimeOption = new("--worker-runtime")
        {
            Description = $"Runtime framework for the functions.",
            HelpName = "dotnet-isolated | dotnet | node | python | powershell | custom"
        };

        public static readonly Argument<string> ConfigurationProfileName = new("PROFILE NAME")
        {
            Description = "The name of the configuration profile to apply."
        };

        public static readonly Lazy<Command> Command = new(ConstructCommand);

        public Command GetCommand()
        {
            return Command.Value;
        }

        private static Command ConstructCommand()
        {
            var action = new ConfigurationProfileAction();
            Command cliCommand = new(action.Name, action.Description);

            cliCommand.Options.Add(WorkerRuntimeOption);
            cliCommand.Arguments.Add(ConfigurationProfileName);
            cliCommand.SetAction(action.Run);

            return cliCommand;
        }
    }
}
