// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    // ShowInHelp is false since .NET does not have any custom arguments
    [Action(Name = "pack dotnet", ParentCommandName = "pack", ShowInHelp = false, HelpText = "Arguments specific to .NET apps when running func pack")]
    internal class DotnetPackSubcommandAction : PackSubcommandAction
    {
        private bool ArtifactsPathSpecified { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            // .NET doesn't have any runtime-specific arguments beyond the common ones
            return base.ParseArgs(args);
        }

        public async Task RunAsync(PackOptions packOptions)
        {
            await ExecuteAsync(packOptions);
        }

        public override Task RunAsync()
        {
            // Keep this in case the customer tries to run func pack dotnet, since this subcommand is not meant to be run directly.
            return Task.CompletedTask;
        }

        protected override void ValidateFunctionApp(string functionAppRoot, PackOptions options)
        {
            var requiredFiles = new[] { "host.json" };
            foreach (var file in requiredFiles)
            {
                if (!FileSystemHelpers.FileExists(Path.Combine(functionAppRoot, file)))
                {
                    throw new CliException($"Required file '{file}' not found in build output directory: {functionAppRoot}");
                }
            }
        }

        protected override async Task<string> GetPackingRootAsync(string functionAppRoot, PackOptions options)
        {
            // ValidateFunctionApp
            PackHelpers.ValidateFunctionAppRoot(functionAppRoot);

            // Get the artifacts path from MSBuild properties if it exists
            // If the value exists, we perform the build (if specified by the user) in that directory and do not delete the build output
            var artifactsPath = DotnetHelpers.TryGetPropertyValueFromMSBuild(functionAppRoot, "ArtifactsPath");
            ArtifactsPathSpecified = !string.IsNullOrEmpty(artifactsPath);

            // For --no-build, treat FolderPath as the build output directory
            if (options.NoBuild)
            {
                var packingRoot = functionAppRoot;

                if (string.IsNullOrEmpty(options.FolderPath))
                {
                    if (!string.IsNullOrEmpty(artifactsPath))
                    {
                        ColoredConsole.WriteLine("Found ArtifactsPath within Directory.Build.props. Using as build output directory.");
                        packingRoot = artifactsPath;
                    }
                    else
                    {
                    ColoredConsole.WriteLine(WarningColor("No folder path specified. Using current directory as build output directory."));
                    packingRoot = Environment.CurrentDirectory;
                }
                }
                else
                {
                    packingRoot = Path.IsPathRooted(options.FolderPath)
                        ? options.FolderPath
                        : Path.Combine(Environment.CurrentDirectory, options.FolderPath);
                }

                if (!Directory.Exists(packingRoot))
                {
                    throw new CliException($"Build output directory not found: {packingRoot}");
                }

                return packingRoot;
            }
            else
            {
                ColoredConsole.WriteLine("Building .NET project...");
                await RunDotNetPublish(functionAppRoot, artifactsPath);

                return Path.Combine(functionAppRoot, artifactsPath ?? "output");
            }
        }

        protected override Task PerformCleanupAfterPackingAsync(string packingRoot, string functionAppRoot, PackOptions options)
        {
            if (!options.NoBuild && !ArtifactsPathSpecified)
            {
                // If not no-build and if artifacts path was not specified, delete packing root after packing
                FileSystemHelpers.DeleteDirectorySafe(packingRoot);
            }

            return Task.CompletedTask;
        }

        private async Task RunDotNetPublish(string functionAppRoot, string artifactsPath = null)
        {
            DotnetHelpers.EnsureDotnet();

            var outputPath = artifactsPath ?? Path.Combine(functionAppRoot, "output");

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
