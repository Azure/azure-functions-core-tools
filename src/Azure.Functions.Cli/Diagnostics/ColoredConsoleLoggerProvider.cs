using System;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Diagnostics
{
    public class ColoredConsoleLoggerProvider : ILoggerProvider
    {
        private readonly LoggingFilterHelper _loggingFilterHelper;
        private readonly LoggerFilterOptions _loggerFilterOptions;
        private readonly string _jsonOutputFilePath;

        public ColoredConsoleLoggerProvider(LoggingFilterHelper loggingFilterHelper, LoggerFilterOptions loggerFilterOptions, string jsonOutputFilePath = null)
        {
            _loggerFilterOptions = loggerFilterOptions ?? throw new ArgumentNullException(nameof(loggerFilterOptions));
            _loggingFilterHelper = loggingFilterHelper ?? throw new ArgumentNullException(nameof(loggingFilterHelper));
            _jsonOutputFilePath = jsonOutputFilePath;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new ColoredConsoleLogger(categoryName, _loggingFilterHelper, _loggerFilterOptions, _jsonOutputFilePath);
        }

        public void Dispose()
        {
        }
    }
}
