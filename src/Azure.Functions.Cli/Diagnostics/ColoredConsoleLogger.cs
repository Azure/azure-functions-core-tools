using System;
using System.Collections.Generic;
using Colors.Net;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Logging;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Diagnostics
{
    public class ColoredConsoleLogger : ILogger
    {
        private readonly Func<string, LogLevel, bool> _filter;
        private readonly string _category;

        public ColoredConsoleLogger(string category, Func<string, LogLevel, bool> filter)
        {
            _category = category;
            _filter = filter;
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

            string formattedMessage = formatter(state, exception);

            if (string.IsNullOrEmpty(formattedMessage))
            {
                return;
            }

            foreach (var line in GetMessageString(logLevel, formattedMessage, exception))
            {
                ColoredConsole.WriteLine($"[{DateTime.UtcNow}] {line}");
            }
        }

        private static IEnumerable<RichString> GetMessageString(LogLevel level, string formattedMessage, Exception exception)
        {
            switch (level)
            {
                case LogLevel.Error:
                    string errorMessage = formattedMessage +
                        Environment.NewLine +
                        (exception == null
                        ? string.Empty
                        : Utility.FlattenException(exception));

                    return SplitAndApply(errorMessage, ErrorColor);
                case LogLevel.Warning:
                    return SplitAndApply(formattedMessage, WarningColor);
                case LogLevel.Information:
                    return SplitAndApply(formattedMessage, AdditionalInfoColor);
                case LogLevel.Debug:
                case LogLevel.Trace:
                    return SplitAndApply(formattedMessage, VerboseColor);
                default:
                    return SplitAndApply(formattedMessage);
            }
        }

        private static IEnumerable<RichString> SplitAndApply(string message, Func<string, RichString> Color = null)
        {
            foreach (var line in message.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.None))
            {
                yield return Color == null ? new RichString(line) : Color(line);
            }
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }
    }
}
