using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Azure.Functions.Cli.Diagnostics
{
    public static class LoggerFactoryExtensions
    {
        public static ILoggerFactory AddColoredConsole(this ILoggerFactory factory, Func<string, LogLevel, bool> filter,  bool includeScopes)
        {
            var provider = new ColoredConsoleLoggerProvider(filter, includeScopes);
            factory.AddProvider(provider);
            return factory;
        }
    }
}
