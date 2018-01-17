using System;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Diagnostics
{
    public class ColoredConsoleLoggerProvider : ILoggerProvider
    {
        private readonly Func<string, LogLevel, bool> _filter;

        public ColoredConsoleLoggerProvider(Func<string, LogLevel, bool> filter)
        {
            _filter = filter;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new ColoredConsoleLogger(categoryName, _filter);
        }

        public void Dispose()
        {
        }
    }
}
