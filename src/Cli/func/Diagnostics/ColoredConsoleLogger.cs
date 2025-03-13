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
using System.IO;
using System.Text;
using System.Collections.Concurrent;

namespace Azure.Functions.Cli.Diagnostics
{
    public class ColoredConsoleLogger : ILogger, IDisposable
    {
        private readonly bool _verboseErrors;
        private readonly Action<string> _logJsonOutput;
        private readonly object _fileAccessLock;
        private readonly string _category;
        private readonly LoggingFilterHelper _loggingFilterHelper;
        private readonly LoggerFilterOptions _loggerFilterOptions;
        private readonly string[] allowedLogsPrefixes = new string[] { "Worker process started and initialized.", "Host lock lease acquired by instance ID" };
        private static readonly LoggerRuleSelector RuleSelector = new LoggerRuleSelector();
        private static readonly Type ProviderType = typeof(ColoredConsoleLoggerProvider);
        private readonly FileStream _jsonOutputFileStream;
        private bool _disposed;
        private const string JsonLogPrefix = "azfuncjsonlog:";
        private readonly static ConcurrentDictionary<string, object> _fileAccessLocks = new ConcurrentDictionary<string, object>();

        public ColoredConsoleLogger(string category, LoggingFilterHelper loggingFilterHelper, LoggerFilterOptions loggerFilterOptions, string jsonOutputFilePath = null)
        {
            _category = category;
            _loggerFilterOptions = loggerFilterOptions ?? throw new ArgumentNullException(nameof(loggerFilterOptions));
            _loggingFilterHelper = loggingFilterHelper ?? throw new ArgumentNullException(nameof(loggingFilterHelper));
            _verboseErrors = StaticSettings.IsDebug;
            _logJsonOutput = message => LogToConsole(LogLevel.Information, null, message, false);


            if (jsonOutputFilePath != null)
            {
                if (!File.Exists(jsonOutputFilePath))
                {
                    _jsonOutputFileStream = File.Create(jsonOutputFilePath);
                }
                else
                {
                    _jsonOutputFileStream = File.Open(jsonOutputFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                }

                _logJsonOutput = LogJsonToFile;

                _fileAccessLock = _fileAccessLocks.GetOrAdd(jsonOutputFilePath, s => new object());
            }
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

            if (formattedMessage.StartsWith(JsonLogPrefix, StringComparison.OrdinalIgnoreCase))
            {
                _logJsonOutput(formattedMessage.Remove(0, JsonLogPrefix.Length));
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

        private void LogToConsole(LogLevel logLevel, Exception exception, string formattedMessage, bool includeTimeStamp = true)
        {
            foreach (var line in GetMessageString(logLevel, formattedMessage, exception))
            {
                string prefix = includeTimeStamp
                    ? $"[{DateTime.UtcNow.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffZ", CultureInfo.InvariantCulture)}] ".DarkGray().ToString()
                    : string.Empty;

                ColoredConsole.WriteLine(prefix + line);
            }
        }

        private void LogJsonToFile(string message)
        {
            lock (_fileAccessLock)
            {
                _jsonOutputFileStream.Write(Encoding.UTF8.GetBytes(message + Environment.NewLine));
                _jsonOutputFileStream.Flush();
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

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _jsonOutputFileStream?.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
        }
    }
}
