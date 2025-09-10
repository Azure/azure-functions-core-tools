// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;
using static Colors.Net.StringStaticMethods;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    internal static class PackValidationHelper
    {
        /// <summary>
        /// Writes a standardized header indicating validation has started.
        /// </summary>
        public static void DisplayValidationStart()
        {
            ColoredConsole.WriteLine()
                .WriteLine(DarkYellow("Validating project..."))
                .WriteLine();
        }

        /// <summary>
        /// Writes the result of an individual validation (pass/fail) with an optional error message.
        /// </summary>
        public static void DisplayValidationResult(string validationTitle, bool passed, string errorMessage = null)
        {
            if (string.IsNullOrWhiteSpace(validationTitle))
            {
                return; // Nothing sensible to display
            }

            var status = passed ? Green("PASSED") : Red("FAILED");
            ColoredConsole.WriteLine($"  {validationTitle}: {status}");
            if (!passed && !string.IsNullOrEmpty(errorMessage))
            {
                ColoredConsole.WriteLine($"    {ErrorColor(errorMessage)}");
            }
        }

        /// <summary>
        /// Writes a non-blocking validation warning.
        /// </summary>
        public static void DisplayValidationWarning(string validationTitle, string warningMessage)
        {
            if (string.IsNullOrWhiteSpace(validationTitle) || string.IsNullOrWhiteSpace(warningMessage))
            {
                return;
            }

            var status = Yellow("WARNING");
            ColoredConsole.WriteLine($"  {validationTitle}: {status}");
            ColoredConsole.WriteLine($"    {WarningColor(warningMessage)}");
        }

        /// <summary>
        /// Writes a spacer after validation completes.
        /// </summary>
        public static void DisplayValidationEnd()
        {
            ColoredConsole.WriteLine();
        }

        /// <summary>
        /// Validates that all requiredFiles exist relative to the supplied directory.
        /// </summary>
        /// <param name="directory">Root directory to validate.</param>
        /// <param name="requiredFiles">File names (no wildcards).</param>
        /// <param name="missingFile">Outputs the first missing file when validation fails, otherwise empty.</param>
        /// <returns>True if all files exist; otherwise false.</returns>
        public static bool ValidateRequiredFiles(string directory, string[] requiredFiles, out string missingFile)
        {
            missingFile = string.Empty;

            if (string.IsNullOrWhiteSpace(directory) || requiredFiles is null || requiredFiles.Length == 0)
            {
                return true; // Nothing to validate; treat as success.
            }

            foreach (var file in requiredFiles)
            {
                if (string.IsNullOrWhiteSpace(file))
                {
                    continue;
                }

                var filePath = Path.Combine(directory, file);
                if (!FileSystemHelpers.FileExists(filePath))
                {
                    missingFile = file;
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Validates that at least one first-level subdirectory contains a specific file.
        /// </summary>
        public static bool ValidateAtLeastOneDirectoryContainsFile(string rootDirectory, string fileName)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory) || string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            try
            {
                var directories = FileSystemHelpers.GetDirectories(rootDirectory);
                foreach (var directory in directories)
                {
                    var filePath = Path.Combine(directory, fileName);
                    if (FileSystemHelpers.FileExists(filePath))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                // Swallow but emit verbose diagnostic if running in debug mode.
                if (StaticSettings.IsDebug)
                {
                    ColoredConsole.WriteLine(VerboseColor($"Validation check failed while scanning '{rootDirectory}': {ex.Message}"));
                }
            }

            return false;
        }

        /// <summary>
        /// Validates that a directory exists and contains at least one file or directory.
        /// </summary>
        public static bool ValidateDirectoryNotEmpty(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return false;
            }

            return FileSystemHelpers.EnsureDirectoryNotEmpty(directoryPath);
        }

        /// <summary>
        /// True if the current OS platform is Windows.
        /// </summary>
        public static bool IsRunningOnWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>
        /// Validates structure expected after a dotnet publish for .NET isolated deployments.
        /// </summary>
        public static bool ValidateDotnetIsolatedFolderStructure(string directory, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(directory))
            {
                errorMessage = "Deployment directory path not specified.";
                return false;
            }

            // Required artifacts
            var requiredFiles = new[] { "host.json", "functions.metadata" };
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

        /// <summary>
        /// Validates mutual exclusivity between Python V1 (function.json) and V2 (function_app.py) models.
        /// </summary>
        public static bool ValidatePythonProgrammingModel(string directory, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(directory))
            {
                errorMessage = "Directory path not specified.";
                return false;
            }

            var hasFunctionApp = FileSystemHelpers.FileExists(Path.Combine(directory, "function_app.py"));
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

            if (hasFunctionApp && hasFunctionJson)
            {
                errorMessage = "Cannot mix Python V1 and V2 programming models. Found both 'function_app.py' (V2) and 'function.json' files (V1) in the same project.";
                return false;
            }

            return true;
        }
    }
}
