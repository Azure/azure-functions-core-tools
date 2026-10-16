// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Retains cache failures that the pinned engine logs instead of throwing.
/// </summary>
internal sealed class TemplateStoreEngineDiagnostics(ILoggerFactory loggerFactory) : ILoggerFactory
{
    private readonly ILoggerFactory _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    private readonly ConcurrentQueue<string> _cacheFailures = new();

    public ILogger CreateLogger(string categoryName)
        => new CacheLogger(_loggerFactory.CreateLogger(categoryName),
            categoryName == "Microsoft.TemplateEngine.Edge.Settings.TemplatePackageManager" ? _cacheFailures : null);

    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException("Configure logging at the CLI composition root.");

    public void Dispose()
    {
        // The CLI owns the shared logger factory, not an individual native host.
    }

    public void ThrowIfCacheFailed()
    {
        if (!_cacheFailures.IsEmpty)
        {
            throw new IOException($"Template cache rebuilding failed: {string.Join("; ", _cacheFailures)}");
        }
    }

    private sealed class CacheLogger(ILogger logger, ConcurrentQueue<string>? failures) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => logger.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => failures is not null || logger.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                failures?.Enqueue(formatter(state, exception));
            }

            logger.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}
