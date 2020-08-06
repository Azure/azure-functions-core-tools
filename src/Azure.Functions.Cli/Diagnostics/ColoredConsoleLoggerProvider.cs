using System;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Diagnostics
{
    public class ColoredConsoleLoggerProvider : ILoggerProvider
    {
        private readonly LoggingFilterHelper _loggingFilterHelper;

        public ColoredConsoleLoggerProvider(LoggingFilterHelper loggingFilterHelper)
        {
            _loggingFilterHelper = loggingFilterHelper;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new ColoredConsoleLogger(categoryName, _loggingFilterHelper);
        }

        public void Dispose()
        {
        }
    }
}
