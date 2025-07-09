// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Microsoft.Azure.WebJobs.Script;

namespace Azure.Functions.Cli.Helpers
{
    public static class ScriptHostHelpers
    {
        private static bool s_isHelpRunning = false;

        public static void SetIsHelpRunning()
        {
            s_isHelpRunning = true;
        }

        public static string GetFunctionAppRootDirectory(string startingDirectory, IEnumerable<string> searchFiles = null)
        {
            if (s_isHelpRunning)
            {
                return startingDirectory;
            }

            searchFiles ??= [ScriptConstants.HostMetadataFileName];

            if (searchFiles.Any(file => FileSystemHelpers.FileExists(Path.Combine(startingDirectory, file))))
            {
                return startingDirectory;
            }

            string parent = Path.GetDirectoryName(startingDirectory);

            if (parent == null)
            {
                string files = searchFiles.Aggregate((accum, file) => $"{accum}, {file}");
                throw new CliException($"Unable to find project root. Expecting to find one of {files} in project root.");
            }
            else
            {
                return GetFunctionAppRootDirectory(parent, searchFiles);
            }
        }
    }
}
