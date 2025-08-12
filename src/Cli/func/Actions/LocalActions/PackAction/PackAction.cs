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
            var originalArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();

            // Internally dispatch to runtime-specific subcommand
            await RunRuntimeSpecificPack(workerRuntime, originalArgs);
        }

        private async Task RunRuntimeSpecificPack(WorkerRuntime runtime, string[] args)
        {
            // Internally dispatch to the appropriate subcommand handler
            switch (runtime)
            {
                case WorkerRuntime.Dotnet:
                case WorkerRuntime.DotnetIsolated:
                    var dotnetSubCommand = new DotNetPackSubCommand(_secretsManager, this);
                    await dotnetSubCommand.ParseAndRunAsync(args);
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
                    var genericSubCommand = new GenericPackSubCommand(_secretsManager, this);
                    await genericSubCommand.ParseAndRunAsync(args);
                    break;
            }
        }

        // Common helper methods that all subcommands can use
        public string ResolveFunctionAppRoot()
        {
            return string.IsNullOrEmpty(FolderPath)
                ? ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory)
                : Path.Combine(Environment.CurrentDirectory, FolderPath);
        }

        public string ResolveOutputPath(string functionAppRoot)
        {
            string resolvedPath;
            if (string.IsNullOrEmpty(OutputPath))
            {
                resolvedPath = Path.Combine(Environment.CurrentDirectory, $"{Path.GetFileName(functionAppRoot)}");
            }
            else
            {
                resolvedPath = Path.Combine(Environment.CurrentDirectory, OutputPath);
                if (FileSystemHelpers.DirectoryExists(resolvedPath))
                {
                    resolvedPath = Path.Combine(resolvedPath, $"{Path.GetFileName(functionAppRoot)}");
                }
            }
            return resolvedPath + ".zip";
        }

        public void ValidateFunctionAppRoot(string functionAppRoot)
        {
            if (!FileSystemHelpers.FileExists(Path.Combine(functionAppRoot, ScriptConstants.HostMetadataFileName)))
            {
                throw new CliException($"Can't find {Path.Combine(functionAppRoot, ScriptConstants.HostMetadataFileName)}");
            }
        }

        public void CleanupExistingPackage(string outputPath)
        {
            if (FileSystemHelpers.FileExists(outputPath))
            {
                ColoredConsole.WriteLine($"Deleting the old package {outputPath}");
                try
                {
                    FileSystemHelpers.FileDelete(outputPath);
                }
                catch (Exception)
                {
                    throw new CliException($"Could not delete {outputPath}");
                }
            }
        }

        public async Task CreatePackage(string packingRoot, string outputPath, bool buildNativeDeps = false)
        {
            bool useGoZip = EnvironmentHelper.GetEnvironmentVariableAsBool(Constants.UseGoZip);
            TelemetryHelpers.AddCommandEventToDictionary(TelemetryCommandEvents, "UseGoZip", useGoZip.ToString());

            var stream = await ZipHelper.GetAppZipFile(packingRoot, buildNativeDeps, BuildOption.Default, noBuild: NoBuild);

            ColoredConsole.WriteLine($"Creating a new package {outputPath}");
            await FileSystemHelpers.WriteToFile(outputPath, stream);
        }
    }
}
