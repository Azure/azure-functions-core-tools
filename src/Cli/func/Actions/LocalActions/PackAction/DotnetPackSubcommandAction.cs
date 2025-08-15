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
    [Action(Name = "pack dotnet", ParentCommandName = "pack", ShowInHelp = false, HelpText = "Arguments specific to .NET apps when running func pack")]
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

            if (!Directory.Exists(functionAppRoot))
            {
                throw new CliException($"Directory not found to pack: {functionAppRoot}");
            }

            if (packOptions.NoBuild)
            {
                // For --no-build, treat FolderPath as the build output directory
                if (string.IsNullOrEmpty(packOptions.FolderPath))
                {
                    ColoredConsole.WriteLine(WarningColor("No folder path specified. Using current directory as build output directory."));
                    packingRoot = Environment.CurrentDirectory;
                }
                else
                {
                    packingRoot = Path.IsPathRooted(packOptions.FolderPath)
                        ? packOptions.FolderPath
                        : Path.Combine(Environment.CurrentDirectory, packOptions.FolderPath);
                }

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

            await PackHelpers.CreatePackage(packingRoot, outputPath, packOptions.NoBuild, TelemetryCommandEvents, packOptions.PreserveExecutables);
        }

        public override Task RunAsync()
        {
            // Keep this in case the customer tries to run func pack dotnet, since this subcommand is not meant to be run directly.
            return Task.CompletedTask;
        }

        private void ValidateDotNetPublishDirectory(string path)
        {
            var requiredFiles = new[] { "host.json" };
            foreach (var file in requiredFiles)
            {
                if (!FileSystemHelpers.FileExists(Path.Combine(path, file)))
                {
                    throw new CliException($"Required file '{file}' not found in build output directory: {path}");
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
            var exe = new Executable("dotnet", $"publish --output \"{outputPath}\"", workingDirectory: functionAppRoot);
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
