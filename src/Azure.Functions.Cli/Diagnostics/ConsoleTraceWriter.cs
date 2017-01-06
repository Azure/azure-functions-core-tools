using System;
using System.Diagnostics;
using System.IO;
using Colors.Net;
using Colors.Net.StringColorExtensions;
using Microsoft.Azure.WebJobs.Host;
using static Azure.Functions.Cli.Common.OutputTheme;
using Microsoft.Azure.WebJobs.Script;

namespace Azure.Functions.Cli.Diagnostics
{
    public class ConsoleTraceWriter : TraceWriter
    {
        public ConsoleTraceWriter(TraceLevel level) : base(level)
        {
            // We want to prevent any Console writers added by the core WebJobs SDK
            // from writing to console, so we set our output to the original console TextWriter
            // and replace it with a Null TextWriter
            ColoredConsole.Out = new ColoredConsoleWriter(Console.Out);
            Console.SetOut(TextWriter.Null);
        }

        public override void Trace(TraceEvent traceEvent)
        {
            if (Level >= traceEvent.Level)
            {
                ColoredConsole.WriteLine(GetMessageString(traceEvent));
            }
        }

        private static RichString GetMessageString(TraceEvent traceEvent)
        {
            switch (traceEvent.Level)
            {
                case TraceLevel.Error:
                    string errorMessage = traceEvent.Message + Environment.NewLine + Utility.FlattenException(traceEvent.Exception);
                    return ErrorColor(errorMessage);
                case TraceLevel.Warning:
                    return traceEvent.Message.Yellow();
                case TraceLevel.Info:
                    return AdditionalInfoColor(traceEvent.Message);
                case TraceLevel.Verbose:
                    return VerboseColor(traceEvent.Message);
                default:
                    return traceEvent.Message.White();
            }
        }
    }
}
