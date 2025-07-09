// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Helpers
{
    public static class LanguageWorkerHelper
    {
        private static readonly Dictionary<WorkerRuntime, string> s_map = new Dictionary<WorkerRuntime, string>
        {
            { WorkerRuntime.Node, "languageWorkers:node:arguments" },
            { WorkerRuntime.Python, "languageWorkers:python:arguments" },
            { WorkerRuntime.Java, "languageWorkers:java:arguments" },
            { WorkerRuntime.Powershell, "languageWorkers:powershell:arguments" },
            { WorkerRuntime.Dotnet, string.Empty },
            { WorkerRuntime.Custom, string.Empty },
            { WorkerRuntime.None, string.Empty }
        }
        .Select(p => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? p
            : new KeyValuePair<WorkerRuntime, string>(p.Key, p.Value.Replace(":", "__")))
        .ToDictionary(k => k.Key, v => v.Value);

        public static IReadOnlyDictionary<string, string> GetWorkerConfiguration(string value)
        {
            if (s_map.ContainsKey(GlobalCoreToolsSettings.CurrentWorkerRuntime) && !string.IsNullOrWhiteSpace(value))
            {
                return new Dictionary<string, string>
                {
                    { s_map[GlobalCoreToolsSettings.CurrentWorkerRuntime], value }
                };
            }
            else
            {
                return new Dictionary<string, string>();
            }
        }
    }
}
