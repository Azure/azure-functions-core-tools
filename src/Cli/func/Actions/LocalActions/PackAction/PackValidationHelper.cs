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
        private const string IndentUnit = "  ";

        private static string GetIndent(int level) => string.Concat(Enumerable.Repeat(IndentUnit, level));

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
            ColoredConsole.WriteLine($"{GetIndent(1)}{validationTitle}: {status}");
            if (!passed && !string.IsNullOrEmpty(errorMessage))
            {
                ColoredConsole.WriteLine($"{GetIndent(2)}{ErrorColor(errorMessage)}");
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
            ColoredConsole.WriteLine($"{GetIndent(1)}{validationTitle}: {status}");
            ColoredConsole.WriteLine($"{GetIndent(2)}{WarningColor(warningMessage)}");
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
        /// Runs a required files validation and displays results.
        /// Throws CliException if validation fails.
        /// </summary>
        public static void RunRequiredFilesValidation(string directory, string[] requiredFiles, string validationTitle = "Validate Required Files")
        {
            var isValid = ValidateRequiredFiles(directory, requiredFiles, out string missingFile);
            DisplayValidationResult(
                validationTitle,
                isValid,
                isValid ? null : string.Empty);
            if (!isValid)
            {
                DisplayValidationEnd();
                throw new CliException($"Required file(s) '{missingFile}' not found in {directory}.");
            }
        }

        /// <summary>
        /// Runs a host.json existence validation and displays results.
        /// Throws CliException if validation fails.
        /// </summary>
        public static void RunHostJsonValidation(string directory)
        {
            var hostJsonExists = FileSystemHelpers.FileExists(Path.Combine(directory, Constants.HostJsonFileName));
            DisplayValidationResult(
                $"Validate {Constants.HostJsonFileName}",
                hostJsonExists,
                hostJsonExists ? null : string.Empty);
            if (!hostJsonExists)
            {
                DisplayValidationEnd();
                throw new CliException($"Required file '{Constants.HostJsonFileName}' not found in directory: {directory}");
            }
        }

        /// <summary>
        /// Runs a validation for invalid flag combinations.
        /// Throws CliException if validation fails.
        /// </summary>
        public static void RunInvalidFlagComboValidation(bool condition, string errorMessage, string validationTitle = "Validate Flag Compatibility")
        {
            DisplayValidationResult(
                validationTitle,
                !condition,
                !condition ? null : errorMessage);
            if (condition)
            {
                DisplayValidationEnd();
                throw new CliException(errorMessage);
            }
        }

        /// <summary>
        /// Runs a set of validations, always including host.json validation and wrapping with DisplayValidationStart/End.
        /// </summary>
        public static void RunValidations(string directory, IEnumerable<Action<string>> validations)
        {
            DisplayValidationStart();
            RunHostJsonValidation(directory);
            if (validations != null)
            {
                foreach (var validation in validations)
                {
                    validation(directory);
                }
            }

            DisplayValidationEnd();
        }
    }
}
