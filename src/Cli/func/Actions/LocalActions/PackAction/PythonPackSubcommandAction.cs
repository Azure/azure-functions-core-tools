// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    [Action(Name = "pack python", ParentCommandName = "pack", ShowInHelp = true, HelpText = "Arguments specific to Python apps when running func pack")]
    internal class PythonPackSubcommandAction : PackSubcommandAction
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
                .WithDescription("Builds function app locally using an image that matches the environment used in Azure. " +
                    "When enabled, Core Tools starts a Docker container, builds the app inside that container," +
                    " and creates a ZIP file with all dependencies restored in .python_packages.")
                .Callback(o => BuildNativeDeps = o);

            return base.ParseArgs(args);
        }

        public async Task RunAsync(PackOptions packOptions, string[] args)
        {
            await ExecuteAsync(packOptions, args);
        }

        protected override void ParseSubcommandArgs(string[] args)
        {
            // Parse python-specific args
            ParseArgs(args);
        }

        protected override void ValidateFunctionApp(string functionAppRoot, PackOptions options)
        {
            // ValidateFunctionApp invalid flag combinations
            if (options.NoBuild && BuildNativeDeps)
            {
                throw new CliException("Invalid options: --no-build cannot be used with --build-native-deps.");
            }

            // Windows warning when not using native deps
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !BuildNativeDeps)
            {
                ColoredConsole.WriteLine(WarningColor("Python function apps are supported only on Linux. Please use the --build-native-deps flag" +
                    " when building on windows to ensure dependencies are properly restored."));
            }
        }

        protected override Task<string> GetPackingRootAsync(string functionAppRoot, PackOptions options)
        {
            // Python packs from the function app root
            return Task.FromResult(functionAppRoot);
        }

        protected override Task PackFunctionAsync(string packingRoot, string outputPath, PackOptions options)
        {
            // Include BuildNativeDeps in packaging call
            return PackHelpers.CreatePackage(packingRoot, outputPath, options.NoBuild, TelemetryCommandEvents, BuildNativeDeps);
        }

        public override Task RunAsync()
        {
            // Keep this since this subcommand is not meant to be run directly.
            return Task.CompletedTask;
        }
    }
}
