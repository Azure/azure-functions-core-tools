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
                ? Environment.CurrentDirectory
                : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, folderPath));
        }

        public static string ResolveOutputPath(string functionAppRoot, string outputPath)
        {
            // Default behavior shared by non-Go runtimes: outputPath (when provided) is always
            // treated as a directory; the produced archive is named after the function app folder.
            // Go overrides this in GoPackSubcommandAction to also accept an explicit .zip file path.
            string resolvedPath;
            if (string.IsNullOrEmpty(outputPath))
            {
                resolvedPath = Path.Combine(Environment.CurrentDirectory, $"{Path.GetFileName(functionAppRoot)}");
            }
            else
            {
                resolvedPath = Path.Combine(Environment.CurrentDirectory, outputPath);
                Directory.CreateDirectory(resolvedPath);
                resolvedPath = Path.Combine(resolvedPath, $"{Path.GetFileName(functionAppRoot)}");
            }

            return resolvedPath + ".zip";
        }

        public static void ValidateFunctionAppRoot(string functionAppRoot)
        {
            // host.json is optional and Core Tools no longer adds one, so its absence is not a
            // hard error. The package is built from whatever the project contains.
            if (!FileSystemHelpers.FileExists(Path.Combine(functionAppRoot, ScriptConstants.HostMetadataFileName)))
            {
                ColoredConsole.WriteLine(WarningColor($"No {ScriptConstants.HostMetadataFileName} found in {functionAppRoot}. The package will be created without one."));
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

        public static async Task CreatePackage(string packingRoot, string outputPath, bool noBuild, WorkerRuntime workerRuntime, IDictionary<string, string> telemetryCommandEvents, bool buildNativeDeps = false)
        {
            var stream = await ZipHelper.GetAppZipFile(packingRoot, buildNativeDeps, BuildOption.Default, noBuild: noBuild, workerRuntime);

            ColoredConsole.WriteLine($"Creating a new package {outputPath}");
            await FileSystemHelpers.WriteToFile(outputPath, stream);
        }
    }
}
