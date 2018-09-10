using System;
using System.Collections.Generic;
using System.Linq;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    public enum WorkerRuntime
    {
        None,
        dotnet,
        node,
        python,
        java,
        powershell
    }

    public static class WorkerRuntimeLanguageHelper
    {
        private static readonly IDictionary<WorkerRuntime, IEnumerable<string>> availableWorkersRuntime = new Dictionary<WorkerRuntime, IEnumerable<string>>
        {
            { WorkerRuntime.dotnet, new [] { "c#", "csharp", "f#", "fsharp" } },
            { WorkerRuntime.node, new [] { "js", "javascript" } },
            { WorkerRuntime.python, new []  { "py" } },
            { WorkerRuntime.java, new string[] { } },
            { WorkerRuntime.powershell, new [] { "pwsh" } }
        };

        private static readonly IDictionary<string, WorkerRuntime> normalizeMap = availableWorkersRuntime
            .Select(p => p.Value.Select(v => new { key = v, value = p.Key }).Append(new { key = p.Key.ToString(), value = p.Key }))
            .SelectMany(i => i)
            .ToDictionary(k => k.key, v => v.value, StringComparer.OrdinalIgnoreCase);

        private static readonly IDictionary<WorkerRuntime, string> workerToLanguageMap = new Dictionary<WorkerRuntime, string>
        {
            { WorkerRuntime.dotnet, "c#" },
            { WorkerRuntime.node, "javascript" },
            { WorkerRuntime.python, "python" },
            { WorkerRuntime.powershell, "powershell" }
        };

        public static string AvailableWorkersRuntimeString =>
            string.Join(", ", availableWorkersRuntime.Keys
                .Where(k => k != WorkerRuntime.python)
                .Where(k => k != WorkerRuntime.java)
                .Select(s => s.ToString()));

        public static IEnumerable<WorkerRuntime> AvailableWorkersList => availableWorkersRuntime.Keys
            .Where(k => k != WorkerRuntime.python)
            .Where(k => k != WorkerRuntime.java);

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

        public static WorkerRuntime GetCurrentWorkerRuntimeLanguage(ISecretsManager secretsManager)
        {
            var setting = secretsManager.GetSecrets().FirstOrDefault(s => s.Key.Equals(Constants.FunctionsWorkerRuntime, StringComparison.OrdinalIgnoreCase)).Value;
            try
            {
                return NormalizeWorkerRuntime(setting);
            }
            catch
            {
                return WorkerRuntime.None;
            }
        }

        internal static WorkerRuntime SetWorkerRuntime(ISecretsManager secretsManager, string language)
        {
            var worker = NormalizeWorkerRuntime(language);

            secretsManager.SetSecret(Constants.FunctionsWorkerRuntime, worker.ToString());
            ColoredConsole
                .WriteLine(WarningColor("Starting from 2.0.1-beta.26 it's required to set a language for your project in your settings"))
                .WriteLine(WarningColor($"'{worker}' has been set in your local.settings.json"));

            return worker;
        }

        public static string GetTemplateLanguageFromWorker(WorkerRuntime worker)
        {
            if (!workerToLanguageMap.ContainsKey(worker))
            {
                throw new ArgumentException($"Worker runtime '{worker}' is not a valid worker for a template.");
            }
            return workerToLanguageMap[worker];
        }
    }
}
