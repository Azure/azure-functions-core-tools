using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Helpers
{
    public static class LanguageWorkerHelper
    {
        private readonly static Dictionary<WorkerRuntime, string> _map = new Dictionary<WorkerRuntime, string>
        {
            { WorkerRuntime.node, "languageWorkers:node:arguments" },
            { WorkerRuntime.python, "languageWorkers:python:arguments" },
            { WorkerRuntime.java, "languageWorkers:java:debug" },
            { WorkerRuntime.dotnet, string.Empty },
            { WorkerRuntime.None, string.Empty }
        }
        .Select(p => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? p
            : new KeyValuePair<WorkerRuntime, string>(p.Key, p.Value.Replace(":", "__")))
        .ToDictionary(k => k.Key, v => v.Value);

        public static IReadOnlyDictionary<string, string> GetWorkerConfiguration(WorkerRuntime workerRuntime, string value)
        {
            if (_map.ContainsKey(workerRuntime) && !string.IsNullOrWhiteSpace(value))
            {
                return new Dictionary<string, string>
                {
                    { _map[workerRuntime], value }
                };
            }
            else
            {
                return new Dictionary<string, string>();
            }
        }
    }
}