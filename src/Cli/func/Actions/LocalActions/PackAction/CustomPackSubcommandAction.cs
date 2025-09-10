// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    [Action(Name = "pack custom", ParentCommandName = "pack", ShowInHelp = false, HelpText = "Arguments specific to custom worker runtime apps when running func pack")]
    internal class CustomPackSubcommandAction : PackSubcommandAction
    {
        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            return base.ParseArgs(args);
        }

        public async Task RunAsync(PackOptions packOptions)
        {
            await ExecuteAsync(packOptions);
        }

        protected override Task<string> GetPackingRootAsync(string functionAppRoot, PackOptions options)
        {
            // Custom worker packs from the function app root without extra steps
            return Task.FromResult(functionAppRoot);
        }

        protected override async Task PackFunctionAsync(string packingRoot, string outputPath, PackOptions options)
        {
            // Custom handler specific packing logic
            var gitIgnorePath = Path.Combine(packingRoot, Constants.FuncIgnoreFile);
            GitIgnoreParser ignoreParser = null;
            
            if (FileSystemHelpers.FileExists(gitIgnorePath))
            {
                ignoreParser = new GitIgnoreParser(await FileSystemHelpers.ReadAllTextFromFileAsync(gitIgnorePath));
            }

            bool useGoZip = EnvironmentHelper.GetEnvironmentVariableAsBool(Constants.UseGoZip);
            TelemetryHelpers.AddCommandEventToDictionary(TelemetryCommandEvents, "UseGoZip", useGoZip.ToString());

            // Use the shared custom handler zip creation logic
            var stream = await CustomHandlerPackHelpers.CreateCustomHandlerZipAsync(packingRoot, ignoreParser);

            ColoredConsole.WriteLine($"Creating a new package {outputPath}");
            await FileSystemHelpers.WriteToFile(outputPath, stream);
        }

        public override Task RunAsync()
        {
            // Keep this since this subcommand is not meant to be run directly.
            return Task.CompletedTask;
        }
    }
}
