using System;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Diagnostics
{
    public class ColoredConsoleLoggerProvider : ILoggerProvider
    {
        private readonly Func<string, LogLevel, bool> _filter;
        private readonly LogLevel _logLevel = LogLevel.Information;

        public ColoredConsoleLoggerProvider(Func<string, LogLevel, bool> filter = null)
        {
            _filter = filter;
        }

        public ColoredConsoleLoggerProvider(LogLevel logLevel)
        {
            _logLevel = logLevel;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new ColoredConsoleLogger(categoryName, _filter, _logLevel);
        }

        public void Dispose()
        {
        }
    }
}
