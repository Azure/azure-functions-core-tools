using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Diagnostics;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Azure.Functions.Cli.Helpers
{
    public static class ScriptHostHelpers
    {
        public static FunctionMetadata GetFunctionMetadata(string functionName)
        {
            var functionErrors = new Dictionary<string, Collection<string>>();
            var functions = ScriptHost.ReadFunctionMetadata(new ScriptHostConfiguration(), new ConsoleTraceWriter(TraceLevel.Info), functionErrors);
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
    }
}
