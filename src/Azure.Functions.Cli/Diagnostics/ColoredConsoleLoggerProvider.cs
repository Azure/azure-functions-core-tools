using System;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
            // Restrict any console logging from the ILogger to only the "Function" category, which are logs
            // coming directly from a function. This removes duplicate host logging.
            if (categoryName == LogCategories.Function)
            {
                return new ColoredConsoleLogger(categoryName, _filter);
            }
            else
            {
                return NullLogger.Instance;
            }
        }

        public void Dispose()
        {
        }
    }
}