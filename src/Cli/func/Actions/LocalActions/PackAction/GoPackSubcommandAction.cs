// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    [Action(Name = "pack go", ParentCommandName = "pack", ShowInHelp = false, HelpText = "Arguments specific to Go apps when running func pack")]
    internal class GoPackSubcommandAction : PackSubcommandAction
    {
        public async Task RunAsync(PackOptions packOptions)
        {
            await ExecuteAsync(packOptions);
        }

        protected internal override void ValidateFunctionApp(string functionAppRoot, PackOptions options)
        {
            GoHelpers.AssertGoFunctionAppLayout(functionAppRoot);
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

        // PackFunctionAsync intentionally not overridden — the default flow goes through
        // PackHelpers.CreatePackage → ZipHelper.GetAppZipFile, which has a Go branch
        // that emits the explicit allowlist (host.json + app).
        public override Task RunAsync()
        {
            // Keep this since this subcommand is not meant to be run directly; dispatch happens
            // via PackAction.RunRuntimeSpecificPackAsync, which calls RunAsync(PackOptions) above.
            return Task.CompletedTask;
        }
    }
}
