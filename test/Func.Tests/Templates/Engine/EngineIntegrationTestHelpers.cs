// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

/// <summary>
/// Records every log entry so tests can assert that a malformed template was
/// skipped with a warning rather than swallowed silently.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
        => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        Entries.Enqueue((logLevel, formatter(state, exception)));
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// Wraps a real mount-file reader but throws on its first read to simulate one
/// malformed template. Every subsequent read delegates to the inner reader so
/// the surviving templates still project correctly.
/// </summary>
internal sealed class ThrowOnceMountFileReader(IFuncTemplateMountFileReader inner) : IFuncTemplateMountFileReader
{
    private readonly IFuncTemplateMountFileReader _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private int _calls;

    public string? TryReadFile(ITemplateInfo template, string? mountRelativePath)
    {
        if (Interlocked.Increment(ref _calls) == 1)
        {
            throw new InvalidOperationException("Simulated malformed template.json.");
        }

        return _inner.TryReadFile(template, mountRelativePath);
    }
}
