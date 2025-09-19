// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Colors.Net;
using Fclp;
using Newtonsoft.Json.Linq;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    [Action(Name = "pack python", ParentCommandName = "pack", ShowInHelp = true, HelpText = "Arguments specific to Python apps when running func pack")]
    internal class PythonPackSubcommandAction : PackSubcommandAction
    {
        public bool BuildNativeDeps { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<bool>("build-native-deps")
                .WithDescription("Builds function app locally using an image that matches the environment used in Azure. " +
                "When enabled, Core Tools starts a Docker container, builds the app inside that container," +
                " and creates a ZIP file with all dependencies restored in .python_packages.")
                .Callback(o => BuildNativeDeps = o);

            return base.ParseArgs(args);
        }

        public async Task RunAsync(PackOptions packOptions, string[] args)
        {
            await ExecuteAsync(packOptions, args);
        }

        protected override void ParseSubcommandArgs(string[] args)
        {
            // Parse python-specific args
            ParseArgs(args);
        }

        protected internal override void ValidateFunctionApp(string functionAppRoot, PackOptions options)
        {
            var validations = new List<Action<string>>
            {
                dir => PackValidationHelper.RunInvalidFlagComboValidation(
                    options.NoBuild && BuildNativeDeps,
                    "Invalid options: --no-build cannot be used with --build-native-deps."),
                dir => PackValidationHelper.RunRequiredFilesValidation(dir, new[] { "requirements.txt" }, "Validate Folder Structure"),
                dir =>
                {
                    // Validate .python_packages directory exists and is not empty
                    // Display a warning if missing or empty since this can be expected if dependencies are not installed yet
                    var pythonPackagesPath = Path.Combine(dir, ".python_packages");
                    var hasPythonPackages = FileSystemHelpers.DirectoryExists(pythonPackagesPath);
                    var pythonPackagesNotEmpty = hasPythonPackages && PackValidationHelper.ValidateDirectoryNotEmpty(pythonPackagesPath);
                    if (!hasPythonPackages || !pythonPackagesNotEmpty)
                    {
                        PackValidationHelper.DisplayValidationWarning(
                            "Validation .python_packages directory exists",
                            "Directory '.python_packages' not found or is empty.");
                    }
                    else
                    {
                        PackValidationHelper.DisplayValidationResult("Validation .python_packages directory exists", true);
                    }
                },
                dir => RunPythonProgrammingModelValidation(dir)
            };

            PackValidationHelper.RunValidations(functionAppRoot, validations);
        }

        protected override Task<string> GetPackingRootAsync(string functionAppRoot, PackOptions options)
        {
            // Python packs from the function app root
            return Task.FromResult(functionAppRoot);
        }

        protected override Task PackFunctionAsync(string packingRoot, string outputPath, PackOptions options)
        {
            // Include BuildNativeDeps in packaging call
            return PackHelpers.CreatePackage(packingRoot, outputPath, options.NoBuild, TelemetryCommandEvents, BuildNativeDeps);
        }

        public override Task RunAsync()
        {
            // Keep this since this subcommand is not meant to be run directly.
            return Task.CompletedTask;
        }

        internal static string GetPythonScriptFileName(string directory)
        {
            // Try local.settings.json first
            var fullPath = Path.Combine(directory, Constants.LocalSettingsJsonFileName);
            if (FileSystemHelpers.FileExists(fullPath))
            {
                var fileContent = FileSystemHelpers.ReadAllTextFromFile(fullPath);
                if (!string.IsNullOrEmpty(fileContent))
                {
                    var localSettingsJObject = JObject.Parse(fileContent);
                    return localSettingsJObject?["Values"]?[Constants.PythonScriptFileName]?.Value<string>();
                }
            }

            return null;
        }

        /// <summary>
        /// Validates mutual exclusivity between Python V1 (function.json) and V2 (function_app.py or custom script file) models.
        /// </summary>
        internal static bool ValidatePythonProgrammingModel(string directory, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(directory))
            {
                errorMessage = "Directory path not specified.";
                return false;
            }

            var pythonScriptFileName = GetPythonScriptFileName(directory) ?? "function_app.py";
            var hasFunctionAppFile = FileSystemHelpers.FileExists(Path.Combine(directory, pythonScriptFileName));
            var hasFunctionJson = false;

            // Scan immediate child directories for function.json (V1 model indicator)
            try
            {
                var directories = FileSystemHelpers.GetDirectories(directory);
                foreach (var subDir in directories)
                {
                    if (FileSystemHelpers.FileExists(Path.Combine(subDir, "function.json")))
                    {
                        hasFunctionJson = true;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (StaticSettings.IsDebug)
                {
                    ColoredConsole.WriteLine(VerboseColor($"Directory scan failed during python model validation: {ex.Message}"));
                }
            }

            if (hasFunctionAppFile && hasFunctionJson)
            {
                errorMessage = $"Cannot mix Python V1 and V2 programming models. Found both '{pythonScriptFileName}' (V2) and 'function.json' files (V1) in the same project.";
                return false;
            }

            if (!hasFunctionAppFile && !hasFunctionJson)
            {
                errorMessage = $"Did not find either '{pythonScriptFileName}' (V2) or 'function.json' files (V1). Project must have one of these files.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Runs a Python programming model validation and displays results.
        /// Throws CliException if validation fails.
        /// </summary>
        public static void RunPythonProgrammingModelValidation(string directory)
        {
            var isValidModel = ValidatePythonProgrammingModel(directory, out string modelError);
            PackValidationHelper.DisplayValidationResult(
                "Validate Python Programming Model",
                isValidModel);
            if (!isValidModel)
            {
                PackValidationHelper.DisplayValidationEnd();
                throw new CliException(modelError);
            }
        }
    }
}
