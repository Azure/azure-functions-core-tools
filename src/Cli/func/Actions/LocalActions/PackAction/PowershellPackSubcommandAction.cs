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

        protected override void ValidateFunctionApp(string functionAppRoot, PackOptions options)
        {
            PackValidationHelper.DisplayValidationStart();

            // Validate Folder Structure - check for host.json
            var hostJsonExists = FileSystemHelpers.FileExists(Path.Combine(functionAppRoot, "host.json"));
            PackValidationHelper.DisplayValidationResult(
                "Validate Basic Structure",
                hostJsonExists,
                hostJsonExists ? null : "Required file 'host.json' not found. Ensure this is a valid Azure Functions project.");

            if (!hostJsonExists)
            {
                PackValidationHelper.DisplayValidationEnd();
                throw new CliException($"Required file 'host.json' not found in {functionAppRoot}. PowerShell function apps require host.json.");
            }

            // Validate that at least one folder contains function.json
            var hasFunctionJson = PackValidationHelper.ValidateAtLeastOneDirectoryContainsFile(functionAppRoot, "function.json");
            PackValidationHelper.DisplayValidationResult(
                "Validate Function Structure",
                hasFunctionJson,
                hasFunctionJson ? null : "No 'function.json' files found in subdirectories. PowerShell function apps require at least one function with function.json.");

            if (!hasFunctionJson)
            {
                PackValidationHelper.DisplayValidationEnd();
                throw new CliException($"No 'function.json' files found in subdirectories of {functionAppRoot}. PowerShell function apps require at least one function with function.json.");
            }

            PackValidationHelper.DisplayValidationEnd();
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
