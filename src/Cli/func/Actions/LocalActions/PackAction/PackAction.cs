// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using Microsoft.Azure.WebJobs.Script;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    [Action(Name = "pack", HelpText = "Pack function app into a zip that's ready to run.", ShowInHelp = true)]
    internal class PackAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;

        public PackAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public string FolderPath { get; set; } = string.Empty;

        public string OutputPath { get; set; }

        public bool NoBuild { get; set; }

        public string[] PreserveExecutables { get; set; } = Array.Empty<string>();

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>('o', "output")
                .WithDescription("output path for the packed archive")
                .Callback(o => OutputPath = o);

            Parser
                .Setup<bool>("no-build")
                .WithDescription("Skip running build for specific language if it is required")
                .Callback(n => NoBuild = n);

            Parser
                .Setup<string>("preserve-executables")
                .WithDescription("Comma separated list of executables to indicate which bits are to be set as executable in the zip file.")
                .Callback(p => PreserveExecutables = p.Split(',').Select(s => s.Trim()).ToArray());

            if (args.Any() && !args.First().StartsWith("-"))
            {
                FolderPath = args.First();
            }

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            // Detect the runtime
            var workerRuntime = WorkerRuntimeLanguageHelper.GetCurrentWorkerRuntimeLanguage(_secretsManager);

            // Get the original command line args to pass to subcommands
            var packOptions = new PackOptions
            {
                FolderPath = FolderPath,
                OutputPath = OutputPath,
                NoBuild = NoBuild,
                PreserveExecutables = PreserveExecutables
            };

            // Internally dispatch to runtime-specific subcommand
            await RunRuntimeSpecificPack(workerRuntime, packOptions);
        }

        private async Task RunRuntimeSpecificPack(WorkerRuntime runtime, PackOptions packOptions)
        {
            // Internally dispatch to the appropriate subcommand handler
            switch (runtime)
            {
                case WorkerRuntime.Dotnet:
                case WorkerRuntime.DotnetIsolated:
                    var dotnetSubCommand = new DotnetPackSubcommandAction(_secretsManager);
                    await dotnetSubCommand.RunAsync(packOptions);
                    break;
                /*
                case WorkerRuntime.Python:
                    var pythonSubCommand = new PythonPackSubCommand(_secretsManager, this);
                    await pythonSubCommand.ParseAndRunAsync(args);
                    break;
                case WorkerRuntime.Node:
                    var nodeSubCommand = new NodePackSubCommand(_secretsManager, this);
                    await nodeSubCommand.ParseAndRunAsync(args);
                    break;
                case WorkerRuntime.Java:
                    var javaSubCommand = new JavaPackSubCommand(_secretsManager, this);
                    await javaSubCommand.ParseAndRunAsync(args);
                    break;
                case WorkerRuntime.Powershell:
                    var powershellSubCommand = new PowerShellPackSubCommand(_secretsManager, this);
                    await powershellSubCommand.ParseAndRunAsync(args);
                    break;
                */
                default:
                    // Keep the default behavior for now until we have created subcommands for other runtimes
                    var functionAppRoot = PackHelpers.ResolveFunctionAppRoot(FolderPath);
                    PackHelpers.ValidateFunctionAppRoot(functionAppRoot);

                    var outputPath = PackHelpers.ResolveOutputPath(functionAppRoot, OutputPath);
                    PackHelpers.CleanupExistingPackage(outputPath);

                    if (!NoBuild)
                    {
                        var installExtensionAction = new InstallExtensionAction(_secretsManager, false);
                        await installExtensionAction.RunAsync();
                    }

                    await PackHelpers.CreatePackage(functionAppRoot, outputPath, NoBuild, TelemetryCommandEvents);
                    break;
            }
        }
    }
}
