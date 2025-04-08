using System.Collections.Generic;
using System.IO;
using System.Linq;
using Azure.Functions.Cli.Common;
using Microsoft.Azure.WebJobs.Script;

namespace Azure.Functions.Cli.Helpers
{
    public static class ScriptHostHelpers
    {
        private const System.Diagnostics.TraceLevel DefaultTraceLevel = System.Diagnostics.TraceLevel.Info;
        private static bool _isHelpRunning = false;

        public static void SetIsHelpRunning()
        {
            _isHelpRunning = true;
        }

        public static string GetFunctionAppRootDirectory(string startingDirectory, IEnumerable<string> searchFiles = null)
        {
            if (_isHelpRunning)
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
    }
}
