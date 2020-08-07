using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using Microsoft.Azure.WebJobs.Script;
using static Colors.Net.StringStaticMethods;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "pack", HelpText = "Pack function app into a zip that's ready to run.", ShowInHelp = false)]
    internal class PackAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;

        public string FolderName { get; set; } = string.Empty;
        public string OutputPath { get; set; }
        public bool BuildNativeDeps { get; set; }
        public string AdditionalPackages { get; set; } = string.Empty;
        public bool Squashfs { get; private set; }

        public PackAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>('o', "output")
                .WithDescription("output path for the packed archive")
                .Callback(o => OutputPath = o);
            Parser
                .Setup<bool>("build-native-deps")
                .SetDefault(false)
                .WithDescription("Skips generating .wheels folder when publishing python function apps.")
                .Callback(f => BuildNativeDeps = f);
            Parser
                .Setup<bool>("no-bundler")
                .WithDescription("Skips generating a bundle when publishing python function apps with build-native-deps.")
                .Callback(nb => ColoredConsole.WriteLine(WarningColor($"Warning: Argument {AdditionalInfoColor("--no-bundler")} is deprecated and a no-op. Python function apps are not bundled anymore.")));
            Parser
                .Setup<string>("additional-packages")
                .WithDescription("List of packages to install when building native dependencies. For example: \"python3-dev libevent-dev\"")
                .Callback(p => AdditionalPackages = p);
            Parser
                .Setup<bool>("squashfs")
                .Callback(f => Squashfs = f);

            if (args.Any() && !args.First().StartsWith("-"))
            {
                FolderName = args.First();
            }

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            var functionAppRoot = string.IsNullOrEmpty(FolderName)
                ? Path.Combine(Environment.CurrentDirectory, FolderName)
                : ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory);

            string outputPath;
            if (string.IsNullOrEmpty(OutputPath))
            {
                outputPath = Path.Combine(Environment.CurrentDirectory, $"{Path.GetFileName(functionAppRoot)}");
            }
            else
            {
                outputPath = Path.Combine(Environment.CurrentDirectory, OutputPath);
                if (FileSystemHelpers.DirectoryExists(outputPath))
                {
                    outputPath = Path.Combine(outputPath, $"{Path.GetFileName(functionAppRoot)}");
                }
            }

            if (!FileSystemHelpers.FileExists(Path.Combine(functionAppRoot, ScriptConstants.HostMetadataFileName)))
            {
                throw new CliException($"Can't find {Path.Combine(functionAppRoot, ScriptConstants.HostMetadataFileName)}");
            }

            var workerRuntime = WorkerRuntimeLanguageHelper.GetCurrentWorkerRuntimeLanguage(_secretsManager);
            outputPath += Squashfs ? ".squashfs" : ".zip";
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

            // Restore all valid extensions
            var installExtensionAction = new InstallExtensionAction(_secretsManager, false);
            await installExtensionAction.RunAsync();
            var stream = await ZipHelper.GetAppZipFile(functionAppRoot, BuildNativeDeps, noBuild: false, buildOption: BuildOption.Default, additionalPackages: AdditionalPackages);

            if (Squashfs)
            {
                stream = await PythonHelpers.ZipToSquashfsStream(stream);
            }

            ColoredConsole.WriteLine($"Creating a new package {outputPath}");
            await FileSystemHelpers.WriteToFile(outputPath, stream);
        }
    }
}
