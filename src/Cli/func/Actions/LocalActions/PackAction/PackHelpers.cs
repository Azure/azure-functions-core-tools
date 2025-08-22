// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Colors.Net;
using Microsoft.Azure.WebJobs.Script;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions.PackAction
{
    internal static class PackHelpers
    {
        // Common helper methods that all subcommands can use
        public static string ResolveFunctionAppRoot(string folderPath)
        {
            return string.IsNullOrEmpty(folderPath)
                ? ScriptHostHelpers.GetFunctionAppRootDirectory(Environment.CurrentDirectory)
                : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, folderPath));
        }

        public static string ResolveOutputPath(string functionAppRoot, string outputPath)
        {
            string resolvedPath;
            if (string.IsNullOrEmpty(outputPath))
            {
                resolvedPath = Path.Combine(Environment.CurrentDirectory, $"{Path.GetFileName(functionAppRoot)}");
            }
            else
            {
                resolvedPath = Path.Combine(Environment.CurrentDirectory, outputPath);

                // Create directory if it doesn't exist
                Directory.CreateDirectory(resolvedPath);
                resolvedPath = Path.Combine(resolvedPath, $"{Path.GetFileName(functionAppRoot)}");
            }

            return resolvedPath + ".zip";
        }

        public static void ValidateFunctionAppRoot(string functionAppRoot)
        {
            if (!FileSystemHelpers.FileExists(Path.Combine(functionAppRoot, ScriptConstants.HostMetadataFileName)))
            {
                throw new CliException($"Can't find {Path.Combine(functionAppRoot, ScriptConstants.HostMetadataFileName)}");
            }
        }

        public static void CleanupExistingPackage(string outputPath)
        {
            if (FileSystemHelpers.FileExists(outputPath))
            {
                ColoredConsole.WriteLine($"Deleting the old package {outputPath}");
                try
                {
                    FileSystemHelpers.FileDelete(outputPath);
                }
                catch (Exception)
                {
                    ColoredConsole.WriteLine(WarningColor($"Could not delete {outputPath}"));
                }
            }
        }

        public static async Task CreatePackage(string packingRoot, string outputPath, bool noBuild, IDictionary<string, string> telemetryCommandEvents, bool buildNativeDeps = false)
        {
            bool useGoZip = EnvironmentHelper.GetEnvironmentVariableAsBool(Constants.UseGoZip);
            TelemetryHelpers.AddCommandEventToDictionary(telemetryCommandEvents, "UseGoZip", useGoZip.ToString());

            var stream = await ZipHelper.GetAppZipFile(packingRoot, buildNativeDeps, BuildOption.Default, noBuild: noBuild);

            ColoredConsole.WriteLine($"Creating a new package {outputPath}");
            await FileSystemHelpers.WriteToFile(outputPath, stream);
        }
    }
}
