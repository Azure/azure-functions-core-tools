// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.Commands.Init
{
    internal class InitCommandParser : ICommandParser
    {
        // Common options that should come from the core workload
        public static readonly Option<bool> SourceControlOption = new("--source-control") // deprecate? Default is actually true
        {
            Description = "Initialize source control in current directory via 'git init'. Default is false."
        };

        public static readonly Option<WorkerRuntime> WorkerRuntimeOption = new("--worker-runtime")
        {
            Description = $"Runtime framework for the functions.",
            HelpName = "dotnet-isolated | dotnet | node | python | powershell | custom"
        };

        public static readonly Option<bool> ForceOption = new("--force")
        {
            Description = "Force initializing in a non-empty directory."
        };

        public static readonly Option<bool> DockerOption = new("--docker")
        {
            Description = "Create a Dockerfile based on the selected worker runtime."
        };

        public static readonly Option<bool> DockerOnlyOption = new("--docker-only")
        {
            Description = "Adds a Dockerfile to an existing function app project. Will prompt for worker-runtime if not specified or set in 'local.settings.json'."
        };

        // Non-dotnet language specific options that should come from the respective workloads
        public static readonly Option<bool> NoBundleOption = new("--no-bundle")
        {
            Description = "Do not install bundles?"
        };

        // Legacy, to be deprecated
        public static readonly Option<bool> CsxOption = new("--csx")
        {
            Description = "Use csx dotnet functions."
        };

        // .NET specific options that should come from the dotnet workload
        public static readonly Option<string> TargetFrameworkOption = new("--target-framework")
        {
            Description = $"Initialize a project with the given target framework moniker. Currently supported only when --worker-runtime set to dotnet-isolated or dotnet.",
            HelpName = $"{string.Join(" | ", TargetFrameworkHelper.GetSupportedTargetFrameworks())}"
        };

        // PowerShell specific options that should come from the powershell workload
        public static readonly Option<bool> ManagedDependencies = new("--managed-dependencies")
        {
            Description = "Installs managed dependencies. Currently, only the PowerShell worker runtime supports this functionality."
        };

        // Node specific options that should come from the node workload
        public static readonly Option<bool> SkipNpmInstallOption = new("--skip-npm-install")
        {
            Description = "Skips the npm installation phase when using V4 programming model for NodeJS"
        };

        public static readonly Option<string> LanguageOption = new("--language")
        {
            Description = "Initialize a language specific project. Currently supported when --worker-runtime set to node.",
            HelpName = "typescript | javascript"
        };

        // Python specific options that should come from the python workload
        public static readonly Option<bool> NoDocsOption = new("--no-docs")
        {
            Description = "Do not create getting started documentation file. Currently supported when --worker-runtime set to python."
        };

        // Node & Python specific options that should come from the respective workloads
        public static readonly Option<string> ModelOption = new("--model", ["-m"])
        {
            Description = "Selects the programming model for the function app. Note this flag is now only applicable to Python and JavaScript/TypeScript. Options are V1 and V2 for Python; V3 and V4 for JavaScript/TypeScript. Currently, the V2 and V4 programming models are in preview.",
            HelpName = "V1 | V2 | V3 | V4"
        };

        public static readonly Argument<string> FolderNameArgument = new("FOLDER PATH")
        {
            Description = "The folder path of where to initialize the new Function App. If not specified, the current folder will be used."
        };

        public static readonly Lazy<Command> Command = new(ConstructCommand);

        public Command GetCommand()
        {
            return Command.Value;
        }

        private static Command ConstructCommand()
        {
            WorkerRuntimeOption.AcceptOnlyFromAmong("dotnet-isolated", "dotnet", "node", "python", "powershell", "custom");
            TargetFrameworkOption.AcceptOnlyFromAmong(TargetFrameworkHelper.GetSupportedTargetFrameworks().ToArray());
            LanguageOption.AcceptOnlyFromAmong("javascript", "typescript");
            ModelOption.AcceptOnlyFromAmong("V1", "V2", "V3", "V4");

            var action = new InitAction();
            Command cliCommand = new(action.Name, action.Description);

            cliCommand.Arguments.Add(FolderNameArgument);

            // cliCommand.Options.Add(SourceControlOption);
            cliCommand.Options.Add(WorkerRuntimeOption);
            cliCommand.Options.Add(ForceOption);
            cliCommand.Options.Add(DockerOption);
            cliCommand.Options.Add(DockerOnlyOption);
            cliCommand.Options.Add(NoBundleOption);
            cliCommand.Options.Add(CsxOption);
            cliCommand.Options.Add(TargetFrameworkOption);
            cliCommand.Options.Add(ManagedDependencies);
            cliCommand.Options.Add(SkipNpmInstallOption);
            cliCommand.Options.Add(LanguageOption);
            cliCommand.Options.Add(NoDocsOption);
            cliCommand.Options.Add(ModelOption);

            cliCommand.SetAction(action.Run);

            // trial subcommands for config profile
            cliCommand.Subcommands.Add(ConfigurationProfileCommandParser.Command.Value);

            return cliCommand;
        }
    }
}
