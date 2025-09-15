// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    // ShowInHelp is false since powershell does not have any custom arguments
    [Action(Name = "pack powershell", ParentCommandName = "pack", ShowInHelp = false, HelpText = "Arguments specific to PowerShell apps when running func pack")]
    internal class PowershellPackSubcommandAction : PackSubcommandAction
    {
        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            return base.ParseArgs(args);
        }

        public async Task RunAsync(PackOptions packOptions)
        {
            await ExecuteAsync(packOptions);
        }

        protected internal override void ValidateFunctionApp(string functionAppRoot, PackOptions options)
        {
            var validations = new List<Action<string>>
            {
                dir => PackValidationHelper.RunAtLeastOneDirectoryContainsFileValidation(dir, "function.json", "Validate Function Structure")
            };
            PackValidationHelper.RunValidations(functionAppRoot, validations);
        }

        protected override Task<string> GetPackingRootAsync(string functionAppRoot, PackOptions options)
        {
            // PowerShell packs from the function app root without extra steps
            return Task.FromResult(functionAppRoot);
        }

        public override Task RunAsync()
        {
            // Keep this since this subcommand is not meant to be run directly.
            return Task.CompletedTask;
        }
    }
}
