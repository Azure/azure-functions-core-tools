using System;
using System.Diagnostics;
using System.IO;
using Azure.Functions.Cli.Diagnostics;
using Colors.Net;
using Microsoft.Azure.WebJobs.Script.WebHost;

namespace Azure.Functions.Cli.Common
{
    internal static class SelfHostWebHostSettingsFactory
    {
        public static WebHostSettings Create(TraceLevel consoleTraceLevel, string scriptPath)
        {
            // We want to prevent any Console writers added by the core WebJobs SDK
            // from writing to console, so we set our output to the original console TextWriter
            // and replace it with a Null TextWriter
            ColoredConsole.Out = new ColoredConsoleWriter(Console.Out);
            Console.SetOut(TextWriter.Null);

            return new WebHostSettings
            {
                IsSelfHost = true,
                ScriptPath = scriptPath,
                LogPath = Path.Combine(Path.GetTempPath(), @"LogFiles\Application\Functions"),
                SecretsPath = Path.Combine(Path.GetTempPath(), "secrets", "functions", "secrets"),
                TraceWriter = new ConsoleTraceWriter(consoleTraceLevel),
                LoggerFactoryBuilder = new CliLoggerFactoryBuilder(),
                IsAuthDisabled = true
            };
        }
    }
}
