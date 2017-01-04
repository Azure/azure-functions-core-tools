using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Azure.Functions.Cli.Diagnostics;

namespace Azure.Functions.Cli.Common
{
    internal static class SelfHostWebHostSettingsFactory
    {
        public static WebHostSettings Create(int nodeDebugPort, TraceLevel consoleTraceLevel)
        {
            return new WebHostSettings
            {
                IsSelfHost = true,
                ScriptPath = Path.Combine(Environment.CurrentDirectory),
                LogPath = Path.Combine(Path.GetTempPath(), @"LogFiles\Application\Functions"),
                SecretsPath = Path.Combine(Path.GetTempPath(), "secrets", "functions", "secrets"),
                NodeDebugPort = nodeDebugPort,
                TraceWriter = new ConsoleTraceWriter(consoleTraceLevel)
            };
        }
    }
}
