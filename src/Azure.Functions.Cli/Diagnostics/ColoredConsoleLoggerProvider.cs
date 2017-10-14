using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;

namespace Azure.Functions.Cli.Diagnostics
{
    public class ColoredConsoleLoggerProvider : ILoggerProvider
    {
        private readonly ConsoleLoggerProvider _innerProvider;
        private bool _disposedValue = false;

        public ColoredConsoleLoggerProvider(Func<string, LogLevel, bool> filter, bool includeScopes)
        {
            _innerProvider = new ConsoleLoggerProvider(filter, includeScopes);
        }

        public ILogger CreateLogger(string categoryName)
        {
            ConsoleLogger logger = (ConsoleLogger)_innerProvider.CreateLogger(categoryName);
            logger.Console = new LoggerConsole();
            return logger;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _innerProvider.Dispose();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
