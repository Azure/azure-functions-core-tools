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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
            var scriptHostOptions = new ScriptJobHostOptions
            {
                RootScriptPath = Environment.CurrentDirectory
            };

            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(new ColoredConsoleLoggerProvider((cat, level) => level >= LogLevel.Information));

            var metadataManager = new FunctionMetadataManager(new OptionsWrapper<ScriptJobHostOptions>(scriptHostOptions), loggerFactory);
            var function = metadataManager.Functions.FirstOrDefault(f => f.Name.Equals(functionName, StringComparison.OrdinalIgnoreCase));
            if (function == null)
            {
                var error = metadataManager.Errors
                    .FirstOrDefault(f => f.Key.Equals(functionName, StringComparison.OrdinalIgnoreCase))
                    .Value
                    .Aggregate(string.Empty, (a, b) => string.Join(Environment.NewLine, a, b));
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
