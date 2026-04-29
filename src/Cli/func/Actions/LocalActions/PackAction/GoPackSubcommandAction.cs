// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    [Action(Name = "pack go", ParentCommandName = "pack", ShowInHelp = false, HelpText = "Arguments specific to Go apps when running func pack")]
    internal class GoPackSubcommandAction : PackSubcommandAction
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
            // RunValidations runs the standard host.json check plus our Go-specific checks.
            var validations = new List<Action<string>>
            {
                dir => PackValidationHelper.RunRequiredFilesValidation(dir, new[] { GoHelpers.GoModFileName }, "Validate go.mod"),
            };
            PackValidationHelper.RunValidations(functionAppRoot, validations);
        }

        protected override Task<string> GetPackingRootAsync(string functionAppRoot, PackOptions options)
        {
            // Go packs from the function app root without a separate staging dir.
            return Task.FromResult(functionAppRoot);
        }

        protected override async Task PerformBuildBeforePackingAsync(string packingRoot, string functionAppRoot, PackOptions options)
        {
            if (options.NoBuild)
            {
                // --no-build: trust the user's pre-built binary, but verify it is actually a linux/amd64 ELF.
                GoHelpers.AssertLinuxAmd64Binary(functionAppRoot);
                return;
            }

            await GoHelpers.BuildForLinux(functionAppRoot);
        }

        // No PackFunctionAsync override — the default flow goes through
        // PackHelpers.CreatePackage → ZipHelper.GetAppZipFile, which has a Go branch
        // that emits the explicit allowlist (host.json + app).
        public override Task RunAsync()
        {
            // Subcommand is dispatched via PackAction.RunRuntimeSpecificPackAsync; not invoked directly.
            return Task.CompletedTask;
        }
    }
}
