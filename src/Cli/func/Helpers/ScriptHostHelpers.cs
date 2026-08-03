// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Microsoft.Azure.WebJobs.Script;

namespace Azure.Functions.Cli.Helpers
{
    public static class ScriptHostHelpers
    {
        private const System.Diagnostics.TraceLevel DefaultTraceLevel = System.Diagnostics.TraceLevel.Info;

        public static string GetFunctionAppRootDirectory(string startingDirectory, IEnumerable<string> searchFiles = null)
        {
            if (GlobalCoreToolsSettings.IsHelpRunning)
            {
                return startingDirectory;
            }

            searchFiles = searchFiles ?? new List<string> { ScriptConstants.HostMetadataFileName };

            if (searchFiles.Any(file => FileSystemHelpers.FileExists(Path.Combine(startingDirectory, file))))
            {
                return startingDirectory;
            }

            var parent = Path.GetDirectoryName(startingDirectory);

            if (parent == null)
            {
                var files = searchFiles.Aggregate((accum, file) => $"{accum}, {file}");
                throw new CliException($"Unable to find project root. Expecting to find one of {files} in project root.");
            }
            else
            {
                return GetFunctionAppRootDirectory(parent, searchFiles);
            }
        }

        // host.json is no longer strictly required in a Functions project (the runtime synthesizes a
        // default when it is missing), so publish/pack must be able to locate the project root without
        // it. Callers that have already resolved the worker runtime use this to build a marker list that
        // combines the common Functions project files with a runtime-specific project marker. This is
        // intentionally scoped to publish/pack and is not applied to the default root detection used by
        // other commands (e.g. func start, func init), whose behavior remains host.json based.
        public static List<string> GetProjectRootSearchFiles(WorkerRuntime workerRuntime)
        {
            var searchFiles = new List<string>
            {
                ScriptConstants.HostMetadataFileName,
                Constants.LocalSettingsJsonFileName,
                Constants.FuncIgnoreFile,
            };

            switch (workerRuntime)
            {
                case WorkerRuntime.Python:
                    searchFiles.Add(Constants.PySteinFunctionAppPy);
                    break;
                case WorkerRuntime.Node:
                    searchFiles.Add(Constants.PackageJsonFileName);
                    break;
                case WorkerRuntime.Go:
                    searchFiles.Add(GoHelpers.GoModFileName);
                    break;
            }

            return searchFiles;
        }
    }
}
