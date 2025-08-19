// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Fclp;

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

        private string[] Args { get; set; }

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

            if (args.Any() && !args.First().StartsWith("-"))
            {
                FolderPath = args.First();
            }

            Args = args;

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
                NoBuild = NoBuild
            };

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
