// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    [Action(Name = "pack go", ParentCommandName = "pack", ShowInHelp = false, HelpText = "Arguments specific to Go apps when running func pack")]
    internal class GoPackSubcommandAction : PackSubcommandAction
    {
        public GoPackSubcommandAction()
            : base(WorkerRuntime.Go)
        {
        }

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

        protected override string ResolveOutputPath(string functionAppRoot, string outputPath)
        {
            // If the user pointed --output at something that already exists as a file (not a
            // directory), surface a clear error rather than letting Directory.CreateDirectory
            // fail with an opaque IOException ("Cannot create ... because a file ... exists").
            if (!string.IsNullOrEmpty(outputPath)
                && File.Exists(Path.Combine(Environment.CurrentDirectory, outputPath)))
            {
                throw new CliException(
                    $"--output '{outputPath}' refers to an existing file. " +
                    "Pass an existing or new directory path instead.");
            }

            return base.ResolveOutputPath(functionAppRoot, outputPath);
        }

        // PackFunctionAsync intentionally not overridden — the default flow goes through
        // PackHelpers.CreatePackage → ZipHelper.GetAppZipFile, which has a Go branch
        // that emits the explicit allowlist (host.json + app).
        public override Task RunAsync()
        {
            throw new NotSupportedException(
                $"{nameof(GoPackSubcommandAction)} is not meant to be invoked directly. " +
                $"Dispatch happens via {nameof(PackAction)}.RunRuntimeSpecificPackAsync, which calls {nameof(RunAsync)}({nameof(PackOptions)}).");
        }
    }
}
