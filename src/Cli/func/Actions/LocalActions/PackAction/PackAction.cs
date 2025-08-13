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
    [Action(Name = "pack", HelpText = "Pack function app into a zip that's ready to deploy.", ShowInHelp = true)]
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
                .WithDescription("Specifies the file path where the packed ZIP archive will be created.")
                .Callback(o => OutputPath = o);

            Parser
                .Setup<bool>("no-build")
                .WithDescription("Do not build the project before packaging. Optionally provide a directory when func pack as the first argument that has the build contents." +
                "Otherwise, default is the current directory.")
                .Callback(n => NoBuild = n);

            Parser
                .Setup<string>("preserve-executables")
                .WithDescription("Comma - separated list of executable files to specify which files should be set as executable in the zip archive.")
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
            await RunRuntimeSpecificPackAsync(workerRuntime, packOptions);
        }

        private async Task RunRuntimeSpecificPackAsync(WorkerRuntime runtime, PackOptions packOptions) =>
            await (runtime switch
            {
                WorkerRuntime.Dotnet or WorkerRuntime.DotnetIsolated => new DotnetPackSubcommandAction(_secretsManager).RunAsync(packOptions),
                _ => throw new CliException($"Unsupported runtime: {runtime}")
            });
    }
}
