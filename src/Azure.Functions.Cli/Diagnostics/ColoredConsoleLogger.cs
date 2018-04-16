using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Diagnostics
{
    public class ColoredConsoleLogger : ILogger
    {
        private readonly Func<string, LogLevel, bool> _filter;
        private readonly string _category;
        private readonly TraceWriter _consoleTraceWriter;

        public ColoredConsoleLogger(string category, Func<string, LogLevel, bool> filter)
        {
            _category = category;
            _filter = filter;

            // Use a ConsoleTraceWriter to handle all formatting
            _consoleTraceWriter = new ConsoleTraceWriter(TraceLevel.Verbose);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            if (_filter == null)
            {
                return true;
            }

            return _filter(_category, logLevel);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            // TraceWriter traces are forwarded to ILoggers. Ignore them to prevent duplicate logging.
            if (IsFromTraceWriter(state as IEnumerable<KeyValuePair<string, object>>))
            {
                return;
            }

            string formattedMessage = formatter(state, exception);

            TraceEvent trace = new TraceEvent(GetTraceLevel(logLevel), formattedMessage, exception: exception);
            _consoleTraceWriter.Trace(trace);
        }

        private static TraceLevel GetTraceLevel(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Error:
                    return TraceLevel.Error;
                case LogLevel.Warning:
                    return TraceLevel.Warning;
                case LogLevel.Information:
                    return TraceLevel.Info;
                case LogLevel.Debug:
                case LogLevel.Trace:
                    return TraceLevel.Verbose;
                default:
                    return TraceLevel.Off;
            }
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        private static bool IsFromTraceWriter(IEnumerable<KeyValuePair<string, object>> properties)
        {
            if (properties == null)
            {
                return false;
            }
            else
            {
                return properties.Any(kvp => string.Equals(kvp.Key, ScriptConstants.TracePropertyIsUserTraceKey, StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}