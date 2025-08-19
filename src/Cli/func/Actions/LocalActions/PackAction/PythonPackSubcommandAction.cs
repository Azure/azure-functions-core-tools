// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    [Action(Name = "pack python", ParentCommandName = "pack", ShowInHelp = true, HelpText = "Arguments specific to Python apps when running func pack")]
    internal class PythonPackSubcommandAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;

        public PythonPackSubcommandAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public bool BuildNativeDeps { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<bool>("build-native-deps")
                .WithDescription("Builds function app locally using an image that was previously hosted." +
                " When enabled, core tools launches a docker container with the build env images, builds the function app inside the container," +
                " and creates a ZIP file with all dependencies restored in .python_packages")
                .Callback(o => BuildNativeDeps = o);

            return base.ParseArgs(args);
        }

        public async Task RunAsync(PackOptions packOptions, string[] args)
        {
            ParseArgs(args);

            // Validate invalid flag combinations
            if (packOptions.NoBuild && BuildNativeDeps)
            {
                throw new CliException("Invalid options: --no-build cannot be used with --build-native-deps.");
            }

            var functionAppRoot = PackHelpers.ResolveFunctionAppRoot(packOptions.FolderPath);

            if (!Directory.Exists(functionAppRoot))
            {
                throw new CliException($"Directory not found to pack: {functionAppRoot}");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !BuildNativeDeps)
            {
                ColoredConsole.WriteLine(WarningColor("Python function apps is supported only on Linux. Please use the --build-native-deps flag" +
                    " when building on windows to ensure dependencies are properly restored."));
            }

            var outputPath = PackHelpers.ResolveOutputPath(functionAppRoot, packOptions.OutputPath);
            PackHelpers.CleanupExistingPackage(outputPath);

            await PackHelpers.CreatePackage(functionAppRoot, outputPath, packOptions.NoBuild, TelemetryCommandEvents, BuildNativeDeps);
        }

        public override Task RunAsync()
        {
            // Keep this since this subcommand is not meant to be run directly.
            return Task.CompletedTask;
        }
    }
}
