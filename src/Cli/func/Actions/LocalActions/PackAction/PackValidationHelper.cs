// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    internal static class PackValidationHelper
    {
        public static void DisplayValidationStart()
        {
            ColoredConsole.WriteLine()
                .WriteLine(DarkYellow("Validating project..."))
                .WriteLine();
        }

        public static void DisplayValidationResult(string validationTitle, bool passed, string? errorMessage = null)
        {
            var status = passed ? Green("PASSED") : Red("FAILED");
            ColoredConsole.WriteLine($"  {validationTitle}: {status}");
            
            if (!passed && !string.IsNullOrEmpty(errorMessage))
            {
                ColoredConsole.WriteLine($"    {ErrorColor(errorMessage)}");
            }
        }

        public static void DisplayValidationWarning(string validationTitle, string warningMessage)
        {
            var status = Yellow("WARNING");
            ColoredConsole.WriteLine($"  {validationTitle}: {status}");
            ColoredConsole.WriteLine($"    {WarningColor(warningMessage)}");
        }

        public static void DisplayValidationEnd()
        {
            ColoredConsole.WriteLine();
        }

        /// <summary>
        /// Validates that required files exist in the specified directory
        /// </summary>
        public static bool ValidateRequiredFiles(string directory, string[] requiredFiles, out string missingFile)
        {
            missingFile = string.Empty;
            
            foreach (var file in requiredFiles)
            {
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
        /// Validates that at least one directory contains the specified file
        /// </summary>
        public static bool ValidateAtLeastOneDirectoryContainsFile(string rootDirectory, string fileName)
        {
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
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validates that a directory exists and is not empty
        /// </summary>
        public static bool ValidateDirectoryNotEmpty(string directoryPath)
        {
            return FileSystemHelpers.EnsureDirectoryNotEmpty(directoryPath);
        }

        /// <summary>
        /// Checks if running on Windows OS
        /// </summary>
        public static bool IsRunningOnWindows()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }

        /// <summary>
        /// Validates .NET deployment structure after dotnet publish
        /// </summary>
        public static bool ValidateDotnetFolderStructure(string directory, out string errorMessage)
        {
            errorMessage = string.Empty;
            
            // Check for required files in the deployment payload
            var requiredFiles = new[]
            {
                "host.json",
                "functions.metadata"
            };

            var requiredDirectories = new[]
            {
                ".azurefunctions"
            };

            // Check required files
            foreach (var file in requiredFiles)
            {
                var filePath = Path.Combine(directory, file);
                if (!FileSystemHelpers.FileExists(filePath))
                {
                    errorMessage = $"Required file '{file}' not found in deployment structure. Ensure 'dotnet publish' has been run.";
                    return false;
                }
            }

            // Check required directories
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
        /// Validates Python programming model consistency (V1 vs V2)
        /// </summary>
        public static bool ValidatePythonProgrammingModel(string directory, out string errorMessage)
        {
            errorMessage = string.Empty;
            
            var hasFunctionApp = FileSystemHelpers.FileExists(Path.Combine(directory, "function_app.py"));
            var hasFunctionJson = false;

            // Check if any subdirectories contain function.json (V1 model)
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
            catch
            {
                // If we can't check directories, assume no function.json files
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