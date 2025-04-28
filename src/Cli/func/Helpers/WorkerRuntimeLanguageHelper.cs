// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    public enum WorkerRuntime
    {
        [DisplayString("none")]
        None,
        [DisplayString("dotnet")]
        Dotnet,
        [DisplayString("dotnet-isolated")]
        DotnetIsolated,
        [DisplayString("node")]
        Node,
        [DisplayString("python")]
        Python,
        [DisplayString("java")]
        Java,
        [DisplayString("powershell")]
        Powershell,
        [DisplayString("custom")]
        Custom
    }

    public static class WorkerRuntimeLanguageHelper
    {
        private static readonly IDictionary<WorkerRuntime, IEnumerable<string>> _availableWorkersRuntime = new Dictionary<WorkerRuntime, IEnumerable<string>>
        {
            { WorkerRuntime.DotnetIsolated, new[] { "dotnet-isolated", "c#-isolated", "csharp-isolated", "f#-isolated", "fsharp-isolated" } },
            { WorkerRuntime.Dotnet, new[] { "c#", "csharp", "f#", "fsharp" } },
            { WorkerRuntime.Node, new[] { "js", "javascript", "typescript", "ts" } },
            { WorkerRuntime.Python, new[] { "py" } },
            { WorkerRuntime.Java, new string[] { } },
            { WorkerRuntime.Powershell, new[] { "pwsh" } },
            { WorkerRuntime.Custom, new string[] { } }
        };

        private static readonly IDictionary<string, WorkerRuntime> _normalizeMap = _availableWorkersRuntime
            .SelectMany(p => p.Value.Select(v => new { key = v, value = p.Key }).Append(new { key = p.Key.ToString(), value = p.Key }))
            .ToDictionary(k => k.key, v => v.value, StringComparer.OrdinalIgnoreCase);

        private static readonly IDictionary<WorkerRuntime, string> _workerToDefaultLanguageMap = new Dictionary<WorkerRuntime, string>
        {
            { WorkerRuntime.Dotnet, Constants.Languages.CSharp },
            { WorkerRuntime.DotnetIsolated, Constants.Languages.CSharpIsolated },
            { WorkerRuntime.Node, Constants.Languages.JavaScript },
            { WorkerRuntime.Python, Constants.Languages.Python },
            { WorkerRuntime.Powershell, Constants.Languages.Powershell },
            { WorkerRuntime.Custom, Constants.Languages.Custom },
        };

        private static readonly IDictionary<string, IEnumerable<string>> _languageToAlias = new Dictionary<string, IEnumerable<string>>
        {
            // By default node should map to javascript
            { Constants.Languages.JavaScript, new[] { "js", "node" } },
            { Constants.Languages.TypeScript, new[] { "ts" } },
            { Constants.Languages.Python, new[] { "py" } },
            { Constants.Languages.Powershell, new[] { "pwsh" } },
            { Constants.Languages.CSharp, new[] { "csharp", "dotnet" } },
            { Constants.Languages.CSharpIsolated, new[] { "dotnet-isolated", "dotnetIsolated" } },
            { Constants.Languages.Java, new string[] { } },
            { Constants.Languages.Custom, new string[] { } }
        };

        public static readonly IDictionary<string, string> WorkerRuntimeStringToLanguage = _languageToAlias
            .Select(p => p.Value.Select(v => new { key = v, value = p.Key }).Append(new { key = p.Key.ToString(), value = p.Key }))
            .SelectMany(i => i)
            .ToDictionary(k => k.key, v => v.value, StringComparer.OrdinalIgnoreCase);

        public static readonly IDictionary<WorkerRuntime, IEnumerable<string>> WorkerToSupportedLanguages = new Dictionary<WorkerRuntime, IEnumerable<string>>
        {
            { WorkerRuntime.Node, new[] { Constants.Languages.JavaScript, Constants.Languages.TypeScript } },
            { WorkerRuntime.Dotnet, new[] { Constants.Languages.CSharp, Constants.Languages.FSharp } },
            { WorkerRuntime.DotnetIsolated, new[] { Constants.Languages.CSharpIsolated, Constants.Languages.FSharpIsolated } }
        };

        public static IEnumerable<WorkerRuntime> AvailableWorkersList => _availableWorkersRuntime.Keys
            .Where(k => k != WorkerRuntime.Java);

        public static string AvailableWorkersRuntimeString =>
            string.Join(", ", _availableWorkersRuntime.Keys
                .Where(k => (k != WorkerRuntime.Java))
                .Select(s => s.ToString()))
            .Replace(WorkerRuntime.DotnetIsolated.ToString(), "dotnet-isolated");

        public static string GetRuntimeMoniker(WorkerRuntime workerRuntime)
        {
            switch (workerRuntime)
            {
                case WorkerRuntime.None:
                    return "None";
                case WorkerRuntime.Dotnet:
                    return "dotnet";
                case WorkerRuntime.DotnetIsolated:
                    return "dotnet-isolated";
                case WorkerRuntime.Node:
                    return "node";
                case WorkerRuntime.Python:
                    return "python";
                case WorkerRuntime.Java:
                    return "java";
                case WorkerRuntime.Powershell:
                    return "powershell";
                case WorkerRuntime.Custom:
                    return "custom";
                default:
                    return "None";
            }
        }

        public static IDictionary<WorkerRuntime, string> GetWorkerToDisplayStrings()
        {
            IDictionary<WorkerRuntime, string> workerToDisplayStrings = new Dictionary<WorkerRuntime, string>();
            foreach (WorkerRuntime wr in AvailableWorkersList)
            {
                switch (wr)
                {
                    case WorkerRuntime.Dotnet:
                        workerToDisplayStrings[wr] = "dotnet (in-process model)";
                        break;
                    case WorkerRuntime.DotnetIsolated:
                        workerToDisplayStrings[wr] = "dotnet (isolated worker model)";
                        break;
                    default:
                        workerToDisplayStrings[wr] = wr.ToString();
                        break;
                }
            }

            return workerToDisplayStrings;
        }

        public static WorkerRuntime NormalizeWorkerRuntime(string workerRuntime)
        {
            if (string.IsNullOrWhiteSpace(workerRuntime))
            {
                throw new ArgumentNullException(nameof(workerRuntime), "Worker runtime cannot be null or empty.");
            }
            else if (_normalizeMap.ContainsKey(workerRuntime))
            {
                return _normalizeMap[workerRuntime];
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
            else if (_normalizeMap.ContainsKey(languageString))
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
            return _normalizeMap.Where(p => p.Value == worker).Select(p => p.Key);
        }

        public static WorkerRuntime GetCurrentWorkerRuntimeLanguage(ISecretsManager secretsManager)
        {
            var setting = Environment.GetEnvironmentVariable(Constants.FunctionsWorkerRuntime)
                          ?? secretsManager.GetSecrets().FirstOrDefault(s => s.Key.Equals(Constants.FunctionsWorkerRuntime, StringComparison.OrdinalIgnoreCase)).Value;

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
            var workerRuntime = NormalizeWorkerRuntime(language);
            var runtimeMoniker = GetRuntimeMoniker(workerRuntime);

            secretsManager.SetSecret(Constants.FunctionsWorkerRuntime, runtimeMoniker);

            ColoredConsole
                .WriteLine(WarningColor("Starting from 2.0.1-beta.26 it's required to set a language for your project in your settings."))
                .WriteLine(WarningColor($"Worker runtime '{runtimeMoniker}' has been set in '{SecretsManager.AppSettingsFilePath}'."));

            return workerRuntime;
        }

        public static string GetDefaultTemplateLanguageFromWorker(WorkerRuntime worker)
        {
            if (_workerToDefaultLanguageMap.ContainsKey(worker))
            {
                throw new ArgumentException($"Worker runtime '{worker}' is not a valid worker for a template.");
            }

            return _workerToDefaultLanguageMap[worker];
        }

        public static bool IsDotnet(WorkerRuntime worker)
        {
            return worker == WorkerRuntime.Dotnet || worker == WorkerRuntime.DotnetIsolated;
        }

        public static bool IsDotnetIsolated(WorkerRuntime worker)
        {
            return worker == WorkerRuntime.DotnetIsolated;
        }
    }
}
