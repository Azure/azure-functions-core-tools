using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Extensions;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using Microsoft.Azure.WebJobs.Script;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "pack", HelpText = "Pack function app into a zip that's ready to run.", ShowInHelp = false)]
    internal class PackAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;

        public string FolderName { get; set; } = string.Empty;
        public string OutputPath { get; set; }
        public bool BuildNativeDeps { get; set; }
        public bool BundleDeps { get; set; }
        public string AdditionalPackages { get; set; } = string.Empty;

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
                .Setup<bool>("bundle-deps")
                .SetDefault(false)
                .WithDescription("Tries to bundler dependencies of python function apps with build-native-deps.")
                .Callback(f => BundleDeps = f);

            Parser
                .Setup<string>("additional-packages")
                .WithDescription("List of packages to install when building native dependencies. For example: \"python3-dev libevent-dev\"")
                .Callback(p => AdditionalPackages = p);

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
                outputPath = Path.Combine(Environment.CurrentDirectory, $"{Path.GetFileName(functionAppRoot)}.zip");
            }
            else
            {
                outputPath = Path.Combine(Environment.CurrentDirectory, OutputPath);
                if (FileSystemHelpers.DirectoryExists(outputPath))
                {
                    outputPath = Path.Combine(outputPath, $"{Path.GetFileName(functionAppRoot)}.zip");
                }
            }

            if (!FileSystemHelpers.FileExists(Path.Combine(functionAppRoot, ScriptConstants.HostMetadataFileName)))
            {
                throw new CliException($"Can't find {Path.Combine(functionAppRoot, ScriptConstants.HostMetadataFileName)}");
            }

            var workerRuntime = WorkerRuntimeLanguageHelper.GetCurrentWorkerRuntimeLanguage(_secretsManager);
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
            var zipStream = await ZipHelper.GetAppZipFile(workerRuntime, functionAppRoot, BuildNativeDeps, BundleDeps, additionalPackages: AdditionalPackages);
            ColoredConsole.WriteLine($"Creating a new package {outputPath}");
            await FileSystemHelpers.WriteToFile(outputPath, zipStream);
        }
    }
}