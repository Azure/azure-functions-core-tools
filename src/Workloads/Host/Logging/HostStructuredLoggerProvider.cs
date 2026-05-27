// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Workloads.Host.Logging;

internal sealed class HostStructuredLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly TextWriter? _stdout;
    private readonly TextWriter? _stderr;
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

    public HostStructuredLoggerProvider()
    {
    }

    internal HostStructuredLoggerProvider(TextWriter stdout, TextWriter stderr)
    {
        _stdout = stdout ?? throw new ArgumentNullException(nameof(stdout));
        _stderr = stderr ?? throw new ArgumentNullException(nameof(stderr));
    }

    public ILogger CreateLogger(string categoryName)
        => new HostStructuredLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        => _scopeProvider = scopeProvider ?? throw new ArgumentNullException(nameof(scopeProvider));

    public void Dispose()
    {
    }

    private TextWriter? SelectWriter(LogLevel level)
        => level >= LogLevel.Error ? _stderr : _stdout;

    private void Write<TState>(string categoryName, LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        string message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is null)
        {
            return;
        }

        IReadOnlyDictionary<string, object?> stateProperties = CaptureState(state);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> scopes = CaptureScopes();
        IReadOnlyDictionary<string, object?> attributes = HostLogSemanticEnricher.BuildAttributes(categoryName, eventId, message, stateProperties, scopes);

        HostStructuredEventWriter.WriteLog(categoryName, logLevel, eventId, message, attributes, exception, stateProperties, scopes, SelectWriter(logLevel));
    }

    private IReadOnlyList<IReadOnlyDictionary<string, object?>> CaptureScopes()
    {
        var scopes = new List<IReadOnlyDictionary<string, object?>>();
        _scopeProvider.ForEachScope(static (scope, state) => state.Add(CaptureScope(scope)), scopes);
        return scopes;
    }

    private static IReadOnlyDictionary<string, object?> CaptureScope(object? scope)
    {
        var captured = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (scope is IEnumerable<KeyValuePair<string, object?>> properties)
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> property in properties)
            {
                values[property.Key] = property.Value;
            }

            captured["values"] = values;
        }

        if (scope is not null)
        {
            captured["type"] = scope.GetType().FullName;
            captured["text"] = Convert.ToString(scope, CultureInfo.InvariantCulture);
        }

        return captured;
    }

    private static IReadOnlyDictionary<string, object?> CaptureState<TState>(TState state)
    {
        if (state is IEnumerable<KeyValuePair<string, object?>> properties)
        {
            var captured = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> property in properties)
            {
                captured[property.Key] = property.Value;
            }

            return captured;
        }

        if (state is null)
        {
            return new Dictionary<string, object?>(0);
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["value"] = state,
            ["type"] = state.GetType().FullName,
        };
    }

    private sealed class HostStructuredLogger(HostStructuredLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => provider._scopeProvider.Push(state);

        public bool IsEnabled(LogLevel logLevel)
            => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.Write(categoryName, logLevel, eventId, state, exception, formatter);
        }
    }
}
