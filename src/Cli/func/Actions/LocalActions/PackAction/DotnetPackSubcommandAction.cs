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
    [Action(Name = "pack dotnet", ParentCommandName = "pack", ShowInHelp = false, HelpText = "Arguments specific to .NET apps when running func pack")]
    internal class DotnetPackSubcommandAction : PackSubcommandAction
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
            await ExecuteAsync(packOptions);
        }

        public override Task RunAsync()
        {
            // Keep this in case the customer tries to run func pack dotnet, since this subcommand is not meant to be run directly.
            return Task.CompletedTask;
        }

        protected override void ValidateFunctionApp(string functionAppRoot, PackOptions options)
        {
            PackValidationHelper.DisplayValidationStart();
            
            // Basic validation for host.json existence
            var hostJsonExists = FileSystemHelpers.FileExists(Path.Combine(functionAppRoot, "host.json"));
            PackValidationHelper.DisplayValidationResult(
                "Validate Basic Structure", 
                hostJsonExists,
                hostJsonExists ? null : "Required file 'host.json' not found. Ensure this is a valid Azure Functions project.");

            if (!hostJsonExists)
            {
                PackValidationHelper.DisplayValidationEnd();
                throw new CliException($"Required file 'host.json' not found in directory: {functionAppRoot}");
            }

            // Validate .NET deployment folder structure (after dotnet publish or in --no-build scenario)
            string directoryToValidate = functionAppRoot;
            
            // If --no-build is specified, validate the provided directory structure
            // If build will happen, this validation will run on the publish output
            if (options.NoBuild || Directory.Exists(Path.Combine(functionAppRoot, "output")))
            {
                if (!options.NoBuild && Directory.Exists(Path.Combine(functionAppRoot, "output")))
                {
                    directoryToValidate = Path.Combine(functionAppRoot, "output");
                }

                var isValidStructure = PackValidationHelper.ValidateDotnetFolderStructure(directoryToValidate, out string errorMessage);
                PackValidationHelper.DisplayValidationResult(
                    "Validate Folder Structure",
                    isValidStructure,
                    isValidStructure ? null : errorMessage);

                if (!isValidStructure)
                {
                    PackValidationHelper.DisplayValidationEnd();
                    throw new CliException(errorMessage);
                }
            }
            else
            {
                // If we're going to build, we'll validate the structure after build
                PackValidationHelper.DisplayValidationResult(
                    "Validate Folder Structure",
                    true,
                    null);
            }

            PackValidationHelper.DisplayValidationEnd();
        }

        protected override async Task<string> GetPackingRootAsync(string functionAppRoot, PackOptions options)
        {
            // ValidateFunctionApp
            PackHelpers.ValidateFunctionAppRoot(functionAppRoot);

            // For --no-build, treat FolderPath as the build output directory
            if (options.NoBuild)
            {
                var packingRoot = functionAppRoot;

                if (string.IsNullOrEmpty(options.FolderPath))
                {
                    ColoredConsole.WriteLine(WarningColor("No folder path specified. Using current directory as build output directory."));
                    packingRoot = Environment.CurrentDirectory;
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
                await RunDotNetPublish(functionAppRoot);

                return Path.Combine(functionAppRoot, "output");
            }
        }

        protected override Task PerformCleanupAfterPackingAsync(string packingRoot, string functionAppRoot, PackOptions options)
        {
            if (!options.NoBuild)
            {
                // If not no-build, delete packing root after packing
                FileSystemHelpers.DeleteDirectorySafe(packingRoot);
            }

            return Task.CompletedTask;
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

            // Validate the published structure
            ColoredConsole.WriteLine();
            ColoredConsole.WriteLine("Validating published output...");
            
            var isValidStructure = PackValidationHelper.ValidateDotnetFolderStructure(outputPath, out string errorMessage);
            PackValidationHelper.DisplayValidationResult(
                "Validate Published Structure",
                isValidStructure,
                isValidStructure ? null : errorMessage);

            if (!isValidStructure)
            {
                throw new CliException($"Published output validation failed: {errorMessage}");
            }
        }
    }
}
