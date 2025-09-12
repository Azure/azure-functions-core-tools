// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    [Action(Name = "pack", HelpText = "Pack function app into a zip that's ready to deploy with optional argument to pass in path of folder to pack.", ShowInHelp = true)]
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
            return new[]
            {
                new CliArgument
                {
                    Name = "PROJECT | SOLUTION",
                    Description = "Folder path of Azure functions project or solution to pack. If a path is not specified, the command will pack the current directory."
                }
            };
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

            // Detect the runtime and set the runtime
            var workerRuntime = WorkerRuntimeLanguageHelper.GetCurrentWorkerRuntimeLanguage(_secretsManager, refreshSecrets: true);

            // If no runtime is detected and NoBuild is true, check for .dll files to infer .NET runtime
            // This is because when we run dotnet publish, there is no local.settings.json anymore to determine runtime.
            if (workerRuntime == WorkerRuntime.None && NoBuild)
            {
                var files = Directory.GetFiles(FolderPath, "*.dll", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    workerRuntime = WorkerRuntime.Dotnet;
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
                WorkerRuntime.Dotnet or WorkerRuntime.DotnetIsolated => new DotnetPackSubcommandAction(_secretsManager).RunAsync(packOptions),
                WorkerRuntime.Python => new PythonPackSubcommandAction(_secretsManager).RunAsync(packOptions, Args),
                WorkerRuntime.Node => new NodePackSubcommandAction(_secretsManager).RunAsync(packOptions, Args),
                WorkerRuntime.Powershell => new PowershellPackSubcommandAction().RunAsync(packOptions),
                WorkerRuntime.Custom => new CustomPackSubcommandAction().RunAsync(packOptions),
                _ => throw new CliException($"Unsupported runtime: {runtime}")
            });
    }
}
