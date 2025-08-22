// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    // Base class for pack subcommands to reduce duplication using a template method.
    internal abstract class PackSubcommandAction : BaseAction
    {
        // Orchestrates the pack flow for subcommands that don't need extra args
        protected async Task ExecuteAsync(PackOptions options)
        {
            var functionAppRoot = PackHelpers.ResolveFunctionAppRoot(options.FolderPath);
            if (!Directory.Exists(functionAppRoot))
            {
                throw new CliException($"Directory not found to pack: {functionAppRoot}");
            }

            ValidateFunctionApp(functionAppRoot, options);

            var packingRoot = await GetPackingRootAsync(functionAppRoot, options);

            var outputPath = PackHelpers.ResolveOutputPath(functionAppRoot, options.OutputPath);
            PackHelpers.CleanupExistingPackage(outputPath);

            await PerformBuildBeforePackingAsync(packingRoot, functionAppRoot, options);

            await PackFunctionAsync(packingRoot, outputPath, options);

            await PerformCleanupAfterPackingAsync(packingRoot, functionAppRoot, options);
        }

        // Orchestrates the pack flow for subcommands that parse extra args
        protected async Task ExecuteAsync(PackOptions options, string[] args)
        {
            ParseSubcommandArgs(args);
            await ExecuteAsync(options);
        }

        // Hook: allow subcommands to parse their specific args
        protected virtual void ParseSubcommandArgs(string[] args)
        {
        }

        // Hook: optional validation prior to determining packing root
        protected virtual void ValidateFunctionApp(string functionAppRoot, PackOptions options)
        {
        }

        // Hook: must return the root folder to package (can trigger build/publish as needed)
        protected abstract Task<string> GetPackingRootAsync(string functionAppRoot, PackOptions options);

        // Hook: optional step to run right before packaging (e.g., Node build)
        protected virtual Task PerformBuildBeforePackingAsync(string packingRoot, string functionAppRoot, PackOptions options) => Task.CompletedTask;

        // Hook: actual packaging operation (default zip from packingRoot)
        protected virtual Task PackFunctionAsync(string packingRoot, string outputPath, PackOptions options)
            => PackHelpers.CreatePackage(packingRoot, outputPath, options.NoBuild, TelemetryCommandEvents);

        // Hook: optional cleanup after packaging
        protected virtual Task PerformCleanupAfterPackingAsync(string packingRoot, string functionAppRoot, PackOptions options) => Task.CompletedTask;
    }
}
