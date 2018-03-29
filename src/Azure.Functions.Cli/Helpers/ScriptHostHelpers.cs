using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Diagnostics;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;

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

        public static FunctionMetadata GetFunctionMetadata(string functionName)
        {
            var functionErrors = new Dictionary<string, Collection<string>>();
            var functions = ScriptHost.ReadFunctionsMetadata(Directory.EnumerateDirectories(Environment.CurrentDirectory), new ColoredConsoleLogger(LogCategories.Startup, (cat, level) => level >= Microsoft.Extensions.Logging.LogLevel.Information), functionErrors);
            var function = functions.FirstOrDefault(f => f.Name.Equals(functionName, StringComparison.OrdinalIgnoreCase));
            if (function == null)
            {
                var error = functionErrors
                    .FirstOrDefault(f => f.Key.Equals(functionName, StringComparison.OrdinalIgnoreCase))
                    .Value
                    ?.Aggregate(string.Empty, (a, b) => string.Join(Environment.NewLine, a, b));
                throw new FunctionNotFoundException($"Unable to get metadata for function {functionName}. Error: {error}");
            }
            else
            {
                return function;
            }
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
