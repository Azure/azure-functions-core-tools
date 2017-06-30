using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Diagnostics;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Helpers
{
    public static class ScriptHostHelpers
    {
        private const TraceLevel DefaultTraceLevel = TraceLevel.Info;
        private static bool _isHelpRunning = false;

        public static void SetIsHelpRunning()
        {
            _isHelpRunning = true;
        }

        public static FunctionMetadata GetFunctionMetadata(string functionName)
        {
            var functionErrors = new Dictionary<string, Collection<string>>();
            var functions = ScriptHost.ReadFunctionMetadata(new ScriptHostConfiguration(), new ConsoleTraceWriter(TraceLevel.Info), null, functionErrors);
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

        public static string GetFunctionAppRootDirectory(string startingDirectory)
        {
            if (_isHelpRunning)
            {
                return startingDirectory;
            }

            var hostJson = Path.Combine(startingDirectory, ScriptConstants.HostMetadataFileName);
            if (FileSystemHelpers.FileExists(hostJson))
            {
                return startingDirectory;
            }

            var parent = Path.GetDirectoryName(startingDirectory);

            if (parent == null)
            {
                throw new CliException($"Unable to find function project root. Expecting to have {ScriptConstants.HostMetadataFileName} in function project root.");
            }
            else
            {
                return GetFunctionAppRootDirectory(parent);
            }
        }

        internal static async Task<TraceLevel> GetTraceLevel(string scriptPath)
        {
            var filePath = Path.Combine(scriptPath, ScriptConstants.HostMetadataFileName);
            if (!FileSystemHelpers.FileExists(filePath))
            {
                return DefaultTraceLevel;
            }

            var hostJson = JsonConvert.DeserializeObject<JObject>(await FileSystemHelpers.ReadAllTextFromFileAsync(filePath));
            var traceLevelStr = hostJson["tracing"]?["consoleLevel"]?.ToString();
            if (!string.IsNullOrEmpty(traceLevelStr) && Enum.TryParse(traceLevelStr, true, out TraceLevel traceLevel))
            {
                return traceLevel;
            }
            else
            {
                return DefaultTraceLevel;
            }
        }
    }
}
