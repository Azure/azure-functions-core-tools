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
    [Action(Name = "pack", HelpText = "Pack Azure Function App into a zip that's ready to deploy.", ShowInHelp = true, HelpOrder = 4)]
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

        private string[] Args { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>('o', "output")
                .WithDescription("Specifies the file path where the packed ZIP archive will be created.")
                .Callback(o => OutputPath = o);

            Parser
                .Setup<bool>("no-build")
                .WithDescription("Do not build the project before packaging. Optionally provide a directory when func pack as the first argument that has the build contents. " +
                "Otherwise, default is the current directory")
                .Callback(n => NoBuild = n);

            if (args.Any() && !args.First().StartsWith("-"))
            {
                FolderPath = args.First();
            }

            Args = args;

            return base.ParseArgs(args);
        }

        public override IEnumerable<CliArgument> GetPositionalArguments()
        {
            return
            [
                new CliArgument
                {
                    Name = "FOLDER PATH",
                    Description = "Folder path of Azure functions project to pack. If a path is not specified, the command will pack the current directory."
                }
            ];
        }

        public override async Task RunAsync()
        {
            // Get the original command line args to pass to subcommands
            var packOptions = new PackOptions
            {
                FolderPath = FolderPath,
                OutputPath = OutputPath,
                NoBuild = NoBuild
            };

            var originalCurrentDirectory = Environment.CurrentDirectory;
            if (!string.IsNullOrEmpty(FolderPath))
            {
                if (!Directory.Exists(FolderPath))
                {
                    throw new CliException($"The specified folder path '{FolderPath}' does not exist.");
                }

                // If a folder path is provided, change to that directory
                Environment.CurrentDirectory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, FolderPath));
            }

            // Detect the runtime from environment variable or local.settings.json
            var workerRuntime = WorkerRuntimeLanguageHelper.GetCurrentWorkerRuntimeLanguage(_secretsManager, refreshSecrets: true);

            // Fall back to project-file inference when the runtime cannot be determined from settings.
            // This is the common case in CI/CD pipelines where local.settings.json is gitignored.
            if (workerRuntime == WorkerRuntime.None)
            {
                ColoredConsole.WriteLine(WarningColor(
                    $"Warning: Could not determine worker runtime from '{Constants.LocalSettingsJsonFileName}' " +
                    $"or '{Constants.FunctionsWorkerRuntime}' environment variable. " +
                    "Attempting to infer from project files..."));

                workerRuntime = InferWorkerRuntimeFromProjectFiles(Environment.CurrentDirectory);

                if (workerRuntime == WorkerRuntime.None)
                {
                    throw new CliException(
                        "Unable to determine the worker runtime for this project. " +
                        $"Set '{Constants.FunctionsWorkerRuntime}' in '{Constants.LocalSettingsJsonFileName}' or as an environment variable. " +
                        $"Valid values: {WorkerRuntimeLanguageHelper.AvailableWorkersRuntimeString}.");
                }

                if (GlobalCoreToolsSettings.IsVerbose)
                {
                    ColoredConsole.WriteLine(VerboseColor(
                        $"Inferred worker runtime '{WorkerRuntimeLanguageHelper.GetRuntimeMoniker(workerRuntime)}' from project files."));
                }
            }

            GlobalCoreToolsSettings.CurrentWorkerRuntime = workerRuntime;

            // Switch back to original directory after detecting runtime to package app in the correct context
            Environment.CurrentDirectory = originalCurrentDirectory;

            // Internally dispatch to runtime-specific subcommand
            await RunRuntimeSpecificPackAsync(workerRuntime, packOptions);
        }

        private async Task RunRuntimeSpecificPackAsync(WorkerRuntime runtime, PackOptions packOptions) =>
            await (runtime switch
            {
                WorkerRuntime.Dotnet or WorkerRuntime.DotnetIsolated => new DotnetPackSubcommandAction(runtime is WorkerRuntime.DotnetIsolated).RunAsync(packOptions),
                WorkerRuntime.Python => new PythonPackSubcommandAction().RunAsync(packOptions, Args),
                WorkerRuntime.Node => new NodePackSubcommandAction(_secretsManager).RunAsync(packOptions, Args),
                WorkerRuntime.Powershell => new PowershellPackSubcommandAction().RunAsync(packOptions),
                WorkerRuntime.Custom => new CustomPackSubcommandAction().RunAsync(packOptions),
                _ => throw new CliException($"Unsupported runtime: {runtime}")
            });

        internal static WorkerRuntime InferWorkerRuntimeFromProjectFiles(string directory)
        {
            // .csproj — check for isolated worker reference to distinguish isolated vs in-proc
            var csprojFiles = Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly);
            if (csprojFiles.Length > 0)
            {
                foreach (var csprojFile in csprojFiles)
                {
                    var content = File.ReadAllText(csprojFile);
                    if (content.Contains("Microsoft.Azure.Functions.Worker", StringComparison.OrdinalIgnoreCase))
                    {
                        return WorkerRuntime.DotnetIsolated;
                    }
                }

                return WorkerRuntime.Dotnet;
            }

            // Python
            if (File.Exists(Path.Combine(directory, Constants.RequirementsTxt)) ||
                File.Exists(Path.Combine(directory, Constants.PySteinFunctionAppPy)))
            {
                return WorkerRuntime.Python;
            }

            // Node
            if (File.Exists(Path.Combine(directory, Constants.PackageJsonFileName)))
            {
                return WorkerRuntime.Node;
            }

            // PowerShell
            if (File.Exists(Path.Combine(directory, "profile.ps1")) ||
                Directory.GetFiles(directory, "*.psd1", SearchOption.TopDirectoryOnly).Length > 0)
            {
                return WorkerRuntime.Powershell;
            }

            // Build-output signals (--no-build scenarios)
            if (File.Exists(Path.Combine(directory, "functions.metadata")))
            {
                return WorkerRuntime.DotnetIsolated;
            }

            if (Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories).Length > 0)
            {
                return WorkerRuntime.Dotnet;
            }

            return WorkerRuntime.None;
        }
    }
}
