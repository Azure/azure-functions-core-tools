using System;
using System.Collections.Generic;
using Colors.Net;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Logging;
using Azure.Functions.Cli.Common;
using static Azure.Functions.Cli.Common.OutputTheme;
using System.Linq;
using Colors.Net.StringColorExtensions;
using System.Globalization;

namespace Azure.Functions.Cli.Diagnostics
{
    public class ColoredConsoleLogger : ILogger
    {
        private readonly bool _verboseErrors;
        private readonly string _category;
        private readonly LoggingFilterHelper _loggingFilterHelper;
        private readonly LoggerFilterOptions _loggerFilterOptions;
        private readonly string[] allowedLogsPrefixes = new string[] { "Worker process started and initialized.", "Host lock lease acquired by instance ID" };
        private static readonly LoggerRuleSelector RuleSelector = new LoggerRuleSelector();
        private static readonly Type ProviderType = typeof(ColoredConsoleLoggerProvider);

        public ColoredConsoleLogger(string category, LoggingFilterHelper loggingFilterHelper, LoggerFilterOptions loggerFilterOptions)
        {
            _category = category;
            _loggerFilterOptions = loggerFilterOptions ?? throw new ArgumentNullException(nameof(loggerFilterOptions));
            _loggingFilterHelper = loggingFilterHelper ?? throw new ArgumentNullException(nameof(loggingFilterHelper));
            _verboseErrors = StaticSettings.IsDebug;
        }

        internal LoggerFilterRule SelectRule(string categoryName, LoggerFilterOptions loggerFilterOptions)
        {
            RuleSelector.Select(loggerFilterOptions, ProviderType, categoryName,
                out LogLevel? minLevel, out Func<string, string, LogLevel, bool> filter);

            return new LoggerFilterRule(ProviderType.FullName, categoryName, minLevel, filter);
        }

        internal bool IsEnabled(string category, LogLevel logLevel)
        {
            LoggerFilterRule filterRule = SelectRule(category, _loggerFilterOptions);

            if (filterRule.LogLevel != null && logLevel < filterRule.LogLevel)
            {
                return false;
            }
            if (filterRule.Filter != null)
            {
                bool enabled = filterRule.Filter(ProviderType.FullName, category, logLevel);
                if (!enabled)
                {
                    return false;
                }
            }
            if (filterRule.LogLevel != null)
            {
                return Utilities.DefaultLoggingFilter(category, logLevel, filterRule.LogLevel.Value, filterRule.LogLevel.Value);
            }
            return Utilities.DefaultLoggingFilter(category, logLevel, _loggingFilterHelper.UserLogDefaultLogLevel, _loggingFilterHelper.SystemLogDefaultLogLevel);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return IsEnabled(_category, logLevel);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            string formattedMessage = formatter(state, exception);

            if (string.IsNullOrEmpty(formattedMessage))
            {
                return;
            }

            if (DoesMessageStartsWithAllowedLogsPrefix(formattedMessage))
            {
                LogToConsole(logLevel, exception, formattedMessage);
                return;
            }

            if (!IsEnabled(logLevel))
            {
                return;
            }

            LogToConsole(logLevel, exception, formattedMessage);
        }

        private void LogToConsole(LogLevel logLevel, Exception exception, string formattedMessage)
        {
            foreach (var line in GetMessageString(logLevel, formattedMessage, exception))
            {
                ColoredConsole.WriteLine($"[{DateTime.UtcNow.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffZ", CultureInfo.InvariantCulture)}] ".DarkGray() + line);
            }
        }

        internal bool DoesMessageStartsWithAllowedLogsPrefix(string formattedMessage)
        {
            if (formattedMessage == null)
            {
                throw new ArgumentNullException(nameof(formattedMessage));
            }
            return allowedLogsPrefixes.Any(s => formattedMessage.StartsWith(s, StringComparison.OrdinalIgnoreCase));
        }

        private IEnumerable<RichString> GetMessageString(LogLevel level, string formattedMessage, Exception exception)
        {
            if (exception != null)
            {
                formattedMessage += Environment.NewLine + (_verboseErrors ? exception.ToString() : Utility.FlattenException(exception));
            }

            switch (level)
            {
                case LogLevel.Error:
                    return SplitAndApply(formattedMessage, ErrorColor);
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
