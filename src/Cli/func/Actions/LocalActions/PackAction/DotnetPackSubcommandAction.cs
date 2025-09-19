// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    [Action(Name = "pack dotnet", ParentCommandName = "pack", ShowInHelp = false, HelpText = "Arguments specific to .NET apps when running func pack")]
    internal class DotnetPackSubcommandAction : PackSubcommandAction
    {
        private readonly bool _isDotnetIsolated;

        public DotnetPackSubcommandAction(bool isDotnetIsolated)
        {
            _isDotnetIsolated = isDotnetIsolated;
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

        protected internal override void ValidateFunctionApp(string functionAppRoot, PackOptions options)
        {
            var validations = new List<Action<string>>();

            // .NET isolated: validate folder structure if --no-build
            if (options.NoBuild && _isDotnetIsolated)
            {
                validations.Add(dir => RunDotnetIsolatedFolderStructureValidation(dir));
            }

            PackValidationHelper.RunValidations(functionAppRoot, validations);
        }

        internal static bool ValidateDotnetIsolatedFolderStructure(string directory, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(directory))
            {
                errorMessage = "Deployment directory path not specified.";
                return false;
            }

            // Required artifacts
            var requiredFiles = new[] { "functions.metadata" };
            var requiredDirectories = new[] { ".azurefunctions" };

            // Validate files
            foreach (var file in requiredFiles)
            {
                var filePath = Path.Combine(directory, file);
                if (!FileSystemHelpers.FileExists(filePath))
                {
                    errorMessage = $"Required file '{file}' not found in deployment structure. Ensure 'dotnet publish' has been run.";
                    return false;
                }
            }

            // Validate directories
            foreach (var dir in requiredDirectories)
            {
                var dirPath = Path.Combine(directory, dir);
                if (!FileSystemHelpers.DirectoryExists(dirPath))
                {
                    errorMessage = $"Required directory '{dir}' not found in deployment structure. Ensure 'dotnet publish' has been run.";
                    return false;
                }
            }

            return true;
        }

        private static void RunDotnetIsolatedFolderStructureValidation(string directory)
        {
            var isValidStructure = ValidateDotnetIsolatedFolderStructure(directory, out string errorMessage);
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

            if (_isDotnetIsolated)
            {
                // Validate the published structure
                ColoredConsole.WriteLine();
                ColoredConsole.WriteLine("Validating published output...");

                var isValidStructure = ValidateDotnetIsolatedFolderStructure(outputPath, out string errorMessage);
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
}
