using System;
using System.Collections.Generic;
using System.Diagnostics;
using Colors.Net;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Diagnostics
{
    public class ConsoleTraceWriter : TraceWriter
    {
        public ConsoleTraceWriter(TraceLevel level) : base(level)
        {
        }

        public override void Trace(TraceEvent traceEvent)
        {
            if (Level >= traceEvent.Level)
            {
                foreach (var line in GetMessageString(traceEvent))
                {
                    ColoredConsole.WriteLine($"[{traceEvent.Timestamp}] {line}");
                }
            }
        }

        private static IEnumerable<RichString> GetMessageString(TraceEvent traceEvent)
        {
            switch (traceEvent.Level)
            {
                case TraceLevel.Error:
                    string errorMessage = traceEvent.Message +
                        Environment.NewLine +
                        (traceEvent.Exception == null
                        ? string.Empty
                        : Utility.FlattenException(traceEvent.Exception));

                    return SplitAndApply(errorMessage, ErrorColor);
                case TraceLevel.Warning:
                    return SplitAndApply(traceEvent.Message, WarningColor);
                case TraceLevel.Info:
                    return SplitAndApply(traceEvent.Message, AdditionalInfoColor);
                case TraceLevel.Verbose:
                    return SplitAndApply(traceEvent.Message, VerboseColor);
                default:
                    return SplitAndApply(traceEvent.Message);
            }
        }

        private static IEnumerable<RichString> SplitAndApply(string message, Func<string, RichString> Color = null)
        {
            foreach (var line in message.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.None))
            {
                yield return Color == null ? new RichString(line) : Color(line);
            }
        }
    }
}
