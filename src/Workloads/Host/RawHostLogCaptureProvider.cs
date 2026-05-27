// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Buffers;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Workloads.Host;

internal sealed class RawHostLogCaptureProvider : ILoggerProvider, ISupportExternalScope
{
    public const string CapturePathEnvironmentVariable = "FUNC_HOST_RAW_LOG_CAPTURE_PATH";

    private const int SchemaVersion = 1;

    private static readonly JsonWriterOptions _writerOptions = new()
    {
        Indented = false,
        SkipValidation = true,
    };

    private readonly object _sync = new();
    private readonly FileStream _stream;
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();
    private bool _disposed;

    public RawHostLogCaptureProvider(string capturePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capturePath);

        string fullPath = Path.GetFullPath(capturePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _stream = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
    }

    internal static bool AddIfEnabled(ILoggingBuilder loggingBuilder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(loggingBuilder);
        ArgumentNullException.ThrowIfNull(configuration);

        string? capturePath = configuration[CapturePathEnvironmentVariable];
        if (string.IsNullOrWhiteSpace(capturePath))
        {
            return false;
        }

        loggingBuilder.Services.AddSingleton<RawHostLogCaptureProvider>(_ => new RawHostLogCaptureProvider(capturePath));
        loggingBuilder.Services.AddSingleton<ILoggerProvider>(static services => services.GetRequiredService<RawHostLogCaptureProvider>());
        loggingBuilder.AddFilter<RawHostLogCaptureProvider>(static logLevel => logLevel >= LogLevel.Trace);
        return true;
    }

    public ILogger CreateLogger(string categoryName)
        => new RawHostLogCaptureLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        => _scopeProvider = scopeProvider ?? throw new ArgumentNullException(nameof(scopeProvider));

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stream.Dispose();
        }
    }

    private void Write<TState>(
        string categoryName,
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        string message = formatter(state, exception);
        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer, _writerOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", SchemaVersion);
            writer.WriteString("timestamp", DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            writer.WriteNumber("process_id", Environment.ProcessId);
            writer.WriteNumber("thread_id", Environment.CurrentManagedThreadId);
            writer.WriteString("category", categoryName);
            writer.WriteString("level", ToLevelString(logLevel));
            WriteEventId(writer, eventId);
            writer.WriteString("message", message);
            WriteState(writer, state);
            WriteScopes(writer);
            if (exception is not null)
            {
                WriteException(writer, exception);
            }

            writer.WriteEndObject();
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _stream.Write(buffer.WrittenSpan);
            _stream.WriteByte((byte)'\n');
            _stream.Flush();
        }
    }

    private void WriteScopes(Utf8JsonWriter writer)
    {
        var scopes = new List<object?>();
        _scopeProvider.ForEachScope(static (scope, state) => state.Add(scope), scopes);

        writer.WriteStartArray("scopes");
        foreach (object? scope in scopes)
        {
            WriteScope(writer, scope);
        }

        writer.WriteEndArray();
    }

    private static void WriteEventId(Utf8JsonWriter writer, EventId eventId)
    {
        writer.WriteStartObject("event_id");
        writer.WriteNumber("id", eventId.Id);
        if (!string.IsNullOrEmpty(eventId.Name))
        {
            writer.WriteString("name", eventId.Name);
        }

        writer.WriteEndObject();
    }

    private static void WriteState<TState>(Utf8JsonWriter writer, TState state)
    {
        writer.WriteStartObject("state");
        if (state is IEnumerable<KeyValuePair<string, object?>> properties)
        {
            foreach (KeyValuePair<string, object?> property in properties)
            {
                WriteProperty(writer, property.Key, property.Value);
            }
        }
        else if (state is not null)
        {
            WriteProperty(writer, "value", state);
            writer.WriteString("type", state.GetType().FullName);
        }

        writer.WriteEndObject();
    }

    private static void WriteScope(Utf8JsonWriter writer, object? scope)
    {
        writer.WriteStartObject();
        if (scope is IEnumerable<KeyValuePair<string, object?>> properties)
        {
            writer.WriteStartObject("values");
            foreach (KeyValuePair<string, object?> property in properties)
            {
                WriteProperty(writer, property.Key, property.Value);
            }

            writer.WriteEndObject();
        }

        if (scope is not null)
        {
            writer.WriteString("type", scope.GetType().FullName);
            writer.WriteString("text", Convert.ToString(scope, CultureInfo.InvariantCulture));
        }

        writer.WriteEndObject();
    }

    private static void WriteException(Utf8JsonWriter writer, Exception exception)
    {
        writer.WriteStartObject("exception");
        writer.WriteString("type", exception.GetType().FullName ?? exception.GetType().Name);
        writer.WriteString("message", exception.Message);
        if (!string.IsNullOrEmpty(exception.StackTrace))
        {
            writer.WriteString("stack", exception.StackTrace);
        }

        writer.WriteEndObject();
    }

    private static void WriteProperty(Utf8JsonWriter writer, string name, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNull(name);
                break;
            case string s:
                writer.WriteString(name, s);
                break;
            case bool b:
                writer.WriteBoolean(name, b);
                break;
            case byte b:
                writer.WriteNumber(name, b);
                break;
            case sbyte b:
                writer.WriteNumber(name, b);
                break;
            case short s:
                writer.WriteNumber(name, s);
                break;
            case ushort s:
                writer.WriteNumber(name, s);
                break;
            case int i:
                writer.WriteNumber(name, i);
                break;
            case uint i:
                writer.WriteNumber(name, i);
                break;
            case long l:
                writer.WriteNumber(name, l);
                break;
            case ulong l:
                writer.WriteNumber(name, l);
                break;
            case float f:
                writer.WriteNumber(name, f);
                break;
            case double d:
                writer.WriteNumber(name, d);
                break;
            case decimal d:
                writer.WriteNumber(name, d);
                break;
            case Guid g:
                writer.WriteString(name, g);
                break;
            case DateTimeOffset dto:
                writer.WriteString(name, dto.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                break;
            case DateTime dt:
                writer.WriteString(name, dt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                break;
            case TimeSpan ts:
                writer.WriteString(name, ts.ToString("c", CultureInfo.InvariantCulture));
                break;
            case Uri uri:
                writer.WriteString(name, uri.ToString());
                break;
            case IEnumerable<KeyValuePair<string, object?>> properties:
                writer.WriteStartObject(name);
                foreach (KeyValuePair<string, object?> property in properties)
                {
                    WriteProperty(writer, property.Key, property.Value);
                }

                writer.WriteEndObject();
                break;
            case IEnumerable values:
                writer.WriteStartArray(name);
                foreach (object? item in values)
                {
                    WriteValue(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                writer.WriteString(name, Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case byte b:
                writer.WriteNumberValue(b);
                break;
            case sbyte b:
                writer.WriteNumberValue(b);
                break;
            case short s:
                writer.WriteNumberValue(s);
                break;
            case ushort s:
                writer.WriteNumberValue(s);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case uint i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case ulong l:
                writer.WriteNumberValue(l);
                break;
            case float f:
                writer.WriteNumberValue(f);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case decimal d:
                writer.WriteNumberValue(d);
                break;
            case Guid g:
                writer.WriteStringValue(g);
                break;
            case DateTimeOffset dto:
                writer.WriteStringValue(dto.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                break;
            case DateTime dt:
                writer.WriteStringValue(dt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                break;
            case TimeSpan ts:
                writer.WriteStringValue(ts.ToString("c", CultureInfo.InvariantCulture));
                break;
            case Uri uri:
                writer.WriteStringValue(uri.ToString());
                break;
            case IEnumerable<KeyValuePair<string, object?>> properties:
                writer.WriteStartObject();
                foreach (KeyValuePair<string, object?> property in properties)
                {
                    WriteProperty(writer, property.Key, property.Value);
                }

                writer.WriteEndObject();
                break;
            case IEnumerable values:
                writer.WriteStartArray();
                foreach (object? item in values)
                {
                    WriteValue(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    private static string ToLevelString(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Information => "information",
        LogLevel.Warning => "warning",
        LogLevel.Error => "error",
        LogLevel.Critical => "critical",
        LogLevel.None => "none",
        _ => level.ToString().ToLowerInvariant(),
    };

    private sealed class RawHostLogCaptureLogger(RawHostLogCaptureProvider provider, string categoryName) : ILogger
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
