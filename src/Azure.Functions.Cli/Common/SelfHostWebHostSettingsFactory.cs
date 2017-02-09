using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Azure.Functions.Cli.Diagnostics;
using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.Common
{
    internal static class SelfHostWebHostSettingsFactory
    {
        public static WebHostSettings Create(TraceLevel consoleTraceLevel, string scriptPath)
        {
            return new WebHostSettings
            {
                IsSelfHost = true,
                ScriptPath = scriptPath,
                LogPath = Path.Combine(Path.GetTempPath(), @"LogFiles\Application\Functions"),
                SecretsPath = Path.Combine(Path.GetTempPath(), "secrets", "functions", "secrets"),
                TraceWriter = new ConsoleTraceWriter(consoleTraceLevel),
                IsAuthDisabled = true
            };
        }
    }
}
