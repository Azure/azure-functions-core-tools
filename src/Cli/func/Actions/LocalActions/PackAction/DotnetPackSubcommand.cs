// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    [Action(Name = "pack dotnet", CommandType = CommandType.SubCommand, ShowInHelp = true, HelpText = "Internal .NET runtime-specific pack command")]
    internal class DotNetPackSubCommand : PackSubCommandBase
    {
        public DotNetPackSubCommand(ISecretsManager secretsManager, PackAction parentAction)
            : base(secretsManager, parentAction)
        {
        }

        protected override void SetupParser()
        {
            // .NET doesn't have any runtime-specific arguments beyond the common ones
            Parser
                .Setup<bool>("build-native-deps")
                .SetDefault(false)
                .WithDescription("Build native dependencies in Docker container");
        }

        public override async Task RunAsync()
        {
            var functionAppRoot = ParentAction.ResolveFunctionAppRoot();
            string packingRoot = functionAppRoot;

            if (ParentAction.NoBuild)
            {
                // For --no-build, treat FolderPath as the build output directory
                if (string.IsNullOrEmpty(ParentAction.FolderPath))
                {
                    throw new CliException("When using --no-build for .NET projects, you must specify the path to the build output directory (e.g., ./bin/Release/net8.0/publish)");
                }

                packingRoot = Path.IsPathRooted(ParentAction.FolderPath)
                    ? ParentAction.FolderPath
                    : Path.Combine(Environment.CurrentDirectory, ParentAction.FolderPath);

                if (!Directory.Exists(packingRoot))
                {
                    throw new CliException($"Build output directory not found: {packingRoot}");
                }

                ValidateDotNetPublishDirectory(packingRoot);
            }
            else
            {
                ParentAction.ValidateFunctionAppRoot(functionAppRoot);

                // Run dotnet publish
                ColoredConsole.WriteLine("Building .NET project...");
                await RunDotNetPublish(functionAppRoot);

                // Update packing root to publish output
                packingRoot = Path.Combine(functionAppRoot, "output");
            }

            var outputPath = ParentAction.ResolveOutputPath(functionAppRoot);
            ParentAction.CleanupExistingPackage(outputPath);

            // Install extensions if not in no-build mode
            if (!ParentAction.NoBuild)
            {
                var installExtensionAction = new InstallExtensionAction(SecretsManager, false);
                await installExtensionAction.RunAsync();
            }

            await ParentAction.CreatePackage(packingRoot, outputPath);
        }

        private void ValidateDotNetPublishDirectory(string path)
        {
            var requiredFiles = new[] { "host.json" };
            foreach (var file in requiredFiles)
            {
                if (!FileSystemHelpers.FileExists(Path.Combine(path, file)))
                {
                    throw new CliException($"Required file {file} not found in build output directory: {path}");
                }
            }
        }

        private async Task RunDotNetPublish(string functionAppRoot)
        {
            DotnetHelpers.EnsureDotnet();

            var outputPath = Path.Combine(functionAppRoot, "output");

            // Clean the output directory if it exists
            if (FileSystemHelpers.DirectoryExists(outputPath))
            {
                FileSystemHelpers.DeleteDirectorySafe(outputPath);
            }

            // Run dotnet publish
            var exe = new Executable("dotnet", $"publish --configuration Release --output \"{outputPath}\"", workingDirectory: functionAppRoot);
            var exitCode = await exe.RunAsync(
                o => ColoredConsole.WriteLine(o),
                e => ColoredConsole.Error.WriteLine(ErrorColor(e)));

            if (exitCode != 0)
            {
                throw new CliException("Error publishing .NET project");
            }
        }
    }
}
