// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    [Action(Name = "pack custom", ParentCommandName = "pack", ShowInHelp = true, HelpText = "Arguments specific to custom worker runtime apps when running func pack")]
    internal class CustomPackSubcommandAction : BaseAction
    {
        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            return base.ParseArgs(args);
        }

        public async Task RunAsync(PackOptions packOptions)
        {
            // No build or specific action required before zipping; just perform zip
            await PackHelpers.DefaultZip(packOptions, TelemetryCommandEvents);
        }

        public override Task RunAsync()
        {
            // Keep this since this subcommand is not meant to be run directly.
            return Task.CompletedTask;
        }
    }
}
