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
        dotnetIsolated,
        node,
        python,
        java,
        powershell,
        custom
    }

    public static class WorkerRuntimeLanguageHelper
    {
        private static readonly IDictionary<WorkerRuntime, IEnumerable<string>> availableWorkersRuntime = new Dictionary<WorkerRuntime, IEnumerable<string>>
        {
            { WorkerRuntime.dotnet, new [] { "c#", "csharp", "f#", "fsharp" } },
            { WorkerRuntime.dotnetIsolated, new [] { "dotnet-isolated" } },
            { WorkerRuntime.node, new [] { "js", "javascript", "typescript", "ts" } },
            { WorkerRuntime.python, new []  { "py" } },
            { WorkerRuntime.java, new string[] { } },
            { WorkerRuntime.powershell, new [] { "pwsh" } },
            { WorkerRuntime.custom, new string[] { } }
        };

        private static readonly IDictionary<string, WorkerRuntime> normalizeMap = availableWorkersRuntime
            .Select(p => p.Value.Select(v => new { key = v, value = p.Key }).Append(new { key = p.Key.ToString(), value = p.Key }))
            .SelectMany(i => i)
            .ToDictionary(k => k.key, v => v.value, StringComparer.OrdinalIgnoreCase);

        private static readonly IDictionary<WorkerRuntime, string> workerToDefaultLanguageMap = new Dictionary<WorkerRuntime, string>
        {
            { WorkerRuntime.dotnet, Constants.Languages.CSharp },
            { WorkerRuntime.node, Constants.Languages.JavaScript },
            { WorkerRuntime.python, Constants.Languages.Python },
            { WorkerRuntime.powershell, Constants.Languages.Powershell },
            { WorkerRuntime.custom, Constants.Languages.Custom },
        };

        private static readonly IDictionary<string, IEnumerable<string>> languageToAlias = new Dictionary<string, IEnumerable<string>>
        {
            // By default node should map to javascript
            { Constants.Languages.JavaScript, new [] { "js", "node" } },
            { Constants.Languages.TypeScript, new [] { "ts" } },
            { Constants.Languages.Python, new [] { "py" } },
            { Constants.Languages.Powershell, new [] { "pwsh" } },
            { Constants.Languages.CSharp, new [] { "csharp", "dotnet" } },
            { Constants.Languages.Java, new string[] { } },
            { Constants.Languages.Custom, new string[] { } }
        };

        public static readonly IDictionary<string, string> WorkerRuntimeStringToLanguage = languageToAlias
            .Select(p => p.Value.Select(v => new { key = v, value = p.Key }).Append(new { key = p.Key.ToString(), value = p.Key }))
            .SelectMany(i => i)
            .ToDictionary(k => k.key, v => v.value, StringComparer.OrdinalIgnoreCase);

        public static readonly IDictionary<WorkerRuntime, IEnumerable<string>> WorkerToSupportedLanguages = new Dictionary<WorkerRuntime, IEnumerable<string>>
        {
            { WorkerRuntime.node, new [] { Constants.Languages.JavaScript, Constants.Languages.TypeScript } }
        };

        public static string AvailableWorkersRuntimeString =>
            string.Join(", ", availableWorkersRuntime.Keys
                .Where(k => (k != WorkerRuntime.java))
                .Where(k => (k != WorkerRuntime.dotnetIsolated))
                .Select(s => s.ToString()));

        public static IEnumerable<WorkerRuntime> AvailableWorkersList => availableWorkersRuntime.Keys
            .Where(k => k != WorkerRuntime.java)
            .Where(k => k != WorkerRuntime.dotnetIsolated);

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

        public static string NormalizeLanguage(string languageString)
        {
            if (string.IsNullOrWhiteSpace(languageString))
            {
                throw new ArgumentNullException(nameof(languageString), "language can't be empty");
            }
            else if (normalizeMap.ContainsKey(languageString))
            {
                return WorkerRuntimeStringToLanguage[languageString];
            }
            else
            {
                throw new ArgumentException($"Language '{languageString}' is not available. Available language strings are {WorkerRuntimeStringToLanguage.Keys}");
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

        public static string GetDefaultTemplateLanguageFromWorker(WorkerRuntime worker)
        {
            if (!workerToDefaultLanguageMap.ContainsKey(worker))
            {
                throw new ArgumentException($"Worker runtime '{worker}' is not a valid worker for a template.");
            }
            return workerToDefaultLanguageMap[worker];
        }

        public static bool IsDotnet(WorkerRuntime worker)
        {
            return worker == WorkerRuntime.dotnet || worker ==  WorkerRuntime.dotnetIsolated;
        }
    }
}
