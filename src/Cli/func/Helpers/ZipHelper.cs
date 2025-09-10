// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Reflection;
using System.Text;
using Azure.Functions.Cli.Common;
using Colors.Net;
using static Colors.Net.StringStaticMethods;

namespace Azure.Functions.Cli.Helpers
{
    public static class ZipHelper
    {
        public static async Task<Stream> GetAppZipFile(string functionAppRoot, bool buildNativeDeps, BuildOption buildOption, bool noBuild, GitIgnoreParser ignoreParser = null, string additionalPackages = null)
        {
            var gitIgnorePath = Path.Combine(functionAppRoot, Constants.FuncIgnoreFile);
            if (ignoreParser == null && FileSystemHelpers.FileExists(gitIgnorePath))
            {
                ignoreParser = new GitIgnoreParser(await FileSystemHelpers.ReadAllTextFromFileAsync(gitIgnorePath));
            }

            if (noBuild)
            {
                ColoredConsole.WriteLine(DarkYellow("Skipping build event for functions project (--no-build)."));
            }
            else if (buildOption == BuildOption.Remote)
            {
                ColoredConsole.WriteLine(DarkYellow("Performing remote build for functions project."));
            }
            else if (buildOption == BuildOption.Local)
            {
                ColoredConsole.WriteLine(DarkYellow("Performing local build for functions project."));
            }

            if (GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.Python && !noBuild)
            {
                return await PythonHelpers.GetPythonDeploymentPackage(FileSystemHelpers.GetLocalFiles(functionAppRoot, ignoreParser), functionAppRoot, buildNativeDeps, buildOption, additionalPackages);
            }
            else if (GlobalCoreToolsSettings.CurrentWorkerRuntime == WorkerRuntime.Dotnet && buildOption == BuildOption.Remote)
            {
                // Remote build for dotnet does not require bin and obj folders. They will be generated during the oryx build
                return await CreateZip(FileSystemHelpers.GetLocalFiles(functionAppRoot, ignoreParser, false, new string[] { "bin", "obj" }), functionAppRoot, Array.Empty<string>());
            }
            else
            {
                // Use the shared helper for custom handler logic
                var executables = await CustomHandlerPackHelpers.GetCustomHandlerExecutablesAsync(functionAppRoot);
                return await CreateZip(FileSystemHelpers.GetLocalFiles(functionAppRoot, ignoreParser, false), functionAppRoot, executables);
            }
        }

        public static async Task<Stream> CreateZip(IEnumerable<string> files, string rootPath, IEnumerable<string> executables)
        {
            // Delegate to the shared custom handler logic for consistent executable handling
            return CustomHandlerPackHelpers.CreateZipWithExecutables(files, rootPath, executables);
        }

        public static bool GoZipExists(out string fileLocation)
        {
            return CustomHandlerPackHelpers.GoZipExists(out fileLocation);
        }

        public static string FixFileNameForZip(this string value, string zipRoot)
        {
            return value.Substring(zipRoot.Length).TrimStart(new[] { '\\', '/' }).Replace('\\', '/');
        }
    }
}
