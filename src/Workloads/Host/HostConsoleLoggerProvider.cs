// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Workloads.Host;

internal sealed class HostConsoleLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
        => new HostConsoleLogger(categoryName);

    public void Dispose()
    {
    }

    private sealed class HostConsoleLogger(string categoryName) : ILogger
    {
        private readonly string _categoryName = categoryName;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel)
            => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            TextWriter writer = logLevel >= LogLevel.Error ? Console.Error : Console.Out;
            string timestamp = DateTime.UtcNow.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffZ", CultureInfo.InvariantCulture);
            writer.WriteLine($"[{timestamp}] {message}");
            if (exception is not null)
            {
                writer.WriteLine(exception);
            }

            writer.Flush();
        }
    }
}
