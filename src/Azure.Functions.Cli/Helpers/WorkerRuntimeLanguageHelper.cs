using System;
using System.Collections.Generic;
using System.Linq;

namespace Azure.Functions.Cli.Helpers
{
    public enum WorkerRuntime
    {
        None,
        dotnet,
        node,
        python
    }

    public static class WorkerRuntimeLanguageHelper
    {
        private static readonly IDictionary<WorkerRuntime, IEnumerable<string>> availableWorkersRuntime = new Dictionary<WorkerRuntime, IEnumerable<string>>
        {
            { WorkerRuntime.dotnet, new [] { "c#", "csharp", "f#", "fsharp" } },
            { WorkerRuntime.node, new [] { "js", "javascript" } },
            { WorkerRuntime.python, new []  { "py" } },
        };

        private static readonly IDictionary<string, WorkerRuntime> normalizeMap = availableWorkersRuntime
            .Select(p => p.Value.Select(v => new { key = v, value = p.Key }).Append(new { key = p.Key.ToString(), value = p.Key }))
            .SelectMany(i => i)
            .ToDictionary(k => k.key, v => v.value, StringComparer.OrdinalIgnoreCase);

        public static string AvailableWorkersRuntimeString =>
            string.Join(", ", availableWorkersRuntime.Keys.Where(k => k != WorkerRuntime.python).Select(s => s.ToString()));

        public static IEnumerable<WorkerRuntime> AvailableWorkersList => availableWorkersRuntime.Keys.Where(k => k != WorkerRuntime.python);

        public static WorkerRuntime NormalizeWorkerRuntime(string workerRuntime)
        {
            if (string.IsNullOrWhiteSpace(workerRuntime))
            {
                throw new ArgumentNullException(nameof(workerRuntime), "worker runtime can't be empty");
            }
            else if (normalizeMap.ContainsKey(workerRuntime))
            {
                return normalizeMap[workerRuntime];
            }
            else
            {
                throw new ArgumentException($"Worker runtime '{workerRuntime}' is not a valid option. Options are {AvailableWorkersRuntimeString}");
            }
        }

        public static IEnumerable<string> LanguagesForWorker(WorkerRuntime worker)
        {
            return normalizeMap.Where(p => p.Value == worker).Select(p => p.Key);
        }
    }
}
