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
    [Action(Name = "pack dotnet", ParentCommandName = "pack", ShowInHelp = false, HelpText = ".NET specific arguments")]
    internal class DotnetPackSubcommandAction : BaseAction
    {
        private readonly ISecretsManager _secretsManager;

        public DotnetPackSubcommandAction(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            // .NET doesn't have any runtime-specific arguments beyond the common ones
            return base.ParseArgs(args);
        }

        public async Task RunAsync(PackOptions packOptions)
        {
            var functionAppRoot = PackHelpers.ResolveFunctionAppRoot(packOptions.FolderPath);
            string packingRoot = functionAppRoot;

            if (packOptions.NoBuild)
            {
                // For --no-build, treat FolderPath as the build output directory
                if (string.IsNullOrEmpty(packOptions.FolderPath))
                {
                    throw new CliException("When using --no-build for .NET projects, you must specify the path to the build output directory (e.g., ./bin/Release/net8.0/publish)");
                }

                packingRoot = Path.IsPathRooted(packOptions.FolderPath)
                    ? packOptions.FolderPath
                    : Path.Combine(Environment.CurrentDirectory, packOptions.FolderPath);

                if (!Directory.Exists(packingRoot))
                {
                    throw new CliException($"Build output directory not found: {packingRoot}");
                }

                ValidateDotNetPublishDirectory(packingRoot);
            }
            else
            {
                PackHelpers.ValidateFunctionAppRoot(functionAppRoot);

                // Run dotnet publish
                ColoredConsole.WriteLine("Building .NET project...");
                await RunDotNetPublish(functionAppRoot);

                // Update packing root to publish output
                packingRoot = Path.Combine(functionAppRoot, "output");
            }

            var outputPath = PackHelpers.ResolveOutputPath(functionAppRoot, packOptions.OutputPath);
            PackHelpers.CleanupExistingPackage(outputPath);

            // Install extensions if not in no-build mode
            if (!packOptions.NoBuild)
            {
                var installExtensionAction = new InstallExtensionAction(_secretsManager, false);
                await installExtensionAction.RunAsync();
            }

            await PackHelpers.CreatePackage(packingRoot, outputPath, packOptions.NoBuild, TelemetryCommandEvents);
        }

        public override Task RunAsync()
        {
            // Keep this in case the customer tries to run func pack dotnet, since this subcommand is not meant to be run directly.
            throw new InvalidOperationException("Invalid command. Please run func pack instead with valid arguments. To see a list of valid arguments, please see func --help.");
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
