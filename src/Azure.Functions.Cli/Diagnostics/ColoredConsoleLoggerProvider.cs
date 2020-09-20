using System;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Diagnostics
{
    public class ColoredConsoleLoggerProvider : ILoggerProvider
    {
        private readonly LoggingFilterHelper _loggingFilterHelper;
        private readonly LoggerFilterOptions _loggerFilterOptions;

        public ColoredConsoleLoggerProvider(LoggingFilterHelper loggingFilterHelper, LoggerFilterOptions loggerFilterOptions)
        {
            _loggerFilterOptions = loggerFilterOptions ?? throw new ArgumentNullException(nameof(loggerFilterOptions));
            _loggingFilterHelper = loggingFilterHelper ?? throw new ArgumentNullException(nameof(loggingFilterHelper));
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new ColoredConsoleLogger(categoryName, _loggingFilterHelper, _loggerFilterOptions);
        }

        public void Dispose()
        {
        }
    }
}
