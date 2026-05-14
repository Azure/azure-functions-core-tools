// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// NDJSON renderer for programmatic consumers and AI agents. Writes one
/// self-contained JSON object per line to <see cref="System.Console.OpenStandardOutput"/>
/// (intentionally bypassing <see cref="IInteractionService"/>, which is
/// Spectre/theme-bound). The schema is versioned (see
/// <c>docs/func-start-json-schema.md</c>) and every record carries
/// <c>schema_version</c> and a <c>kind</c> discriminator.
/// </summary>
internal sealed class JsonRenderer : IDashboardRenderer
{
    private const int SchemaVersion = 1;

    private static readonly JsonWriterOptions _writerOptions = new()
    {
        Indented = false,
        SkipValidation = true,
    };

    private readonly Stream _stdout;
    private readonly bool _ownsStream;
    private readonly object _lock = new();

    public JsonRenderer()
        : this(System.Console.OpenStandardOutput(), ownsStream: false)
    {
    }

    internal JsonRenderer(Stream output, bool ownsStream)
    {
        _stdout = output ?? throw new ArgumentNullException(nameof(output));
        _ownsStream = ownsStream;
    }

    public Task OnStartAsync(DashboardState state, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task OnEventAsync(HostLogEntry entry, IReadOnlyList<DashboardEvent> events, CancellationToken cancellationToken)
    {
        // Always emit the raw log record first so synthetic events appear in
        // causal order after the line that produced them.
        WriteLog(entry);
        foreach (DashboardEvent ev in events)
        {
            WriteEvent(ev);
        }

        return Task.CompletedTask;
    }

    public Task OnSummaryAsync(SummaryEvent summary, CancellationToken cancellationToken)
    {
        WriteRecord(CliEventKinds.Summary, summary.Timestamp, writer =>
        {
            writer.WriteString("exit_reason", summary.ExitReason);
            writer.WriteNumber("uptime_seconds", Math.Round(summary.UptimeSeconds, 3));
            writer.WriteNumber("function_count", summary.FunctionCount);
            writer.WriteStartObject("invocations");
            writer.WriteNumber("total", summary.TotalInvocations);
            writer.WriteNumber("succeeded", summary.SucceededInvocations);
            writer.WriteNumber("failed", summary.FailedInvocations);
            writer.WriteEndObject();
            writer.WriteNumber("errors", summary.ErrorCount);
        });

        Flush();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Flush();
        if (_ownsStream)
        {
            _stdout.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private void WriteLog(HostLogEntry entry)
    {
        WriteRecord(CliEventKinds.Log, entry.Timestamp, writer =>
        {
            writer.WriteString("category", entry.Category);
            writer.WriteString("level", LevelToString(entry.Level));
            writer.WriteNumber("event_id", entry.EventId.Id);
            if (!string.IsNullOrEmpty(entry.EventId.Name))
            {
                writer.WriteString("event_name", entry.EventId.Name);
            }

            writer.WriteString("message", entry.Message);
            if (entry.Exception is not null)
            {
                WriteException(writer, "exception", entry.Exception);
            }

            WriteAttributes(writer, entry.Attributes);
        });
    }

    private void WriteEvent(DashboardEvent ev)
    {
        switch (ev)
        {
            case HostStateChangedEvent hs:
                WriteRecord(CliEventKinds.HostStateChanged, hs.Timestamp, writer =>
                {
                    writer.WriteString("from", hs.From.ToString().ToLowerInvariant());
                    writer.WriteString("to", hs.To.ToString().ToLowerInvariant());
                    if (hs.DurationMs is { } d)
                    {
                        writer.WriteNumber("duration_ms", Math.Round(d, 3));
                    }

                    if (!string.IsNullOrEmpty(hs.Reason))
                    {
                        writer.WriteString("reason", hs.Reason);
                    }

                    if (!string.IsNullOrEmpty(hs.Trigger))
                    {
                        writer.WriteString("trigger", hs.Trigger);
                    }
                });
                break;

            case FunctionDiscoveredEvent fd:
                WriteRecord(CliEventKinds.FunctionDiscovered, fd.Timestamp, writer =>
                {
                    writer.WriteString("name", fd.Function.Name);
                    writer.WriteString("trigger_type", fd.Function.TriggerType);
                    if (!string.IsNullOrEmpty(fd.Function.Route))
                    {
                        writer.WriteString("route", fd.Function.Route);
                    }

                    if (fd.Function.HttpMethods.Count > 0)
                    {
                        writer.WriteStartArray("http_methods");
                        foreach (string m in fd.Function.HttpMethods)
                        {
                            writer.WriteStringValue(m);
                        }

                        writer.WriteEndArray();
                    }
                });
                break;

            case FunctionRemovedEvent fr:
                WriteRecord(CliEventKinds.FunctionRemoved, fr.Timestamp, writer =>
                {
                    writer.WriteString("name", fr.Name);
                });
                break;

            case InvocationStartedEvent inv:
                WriteRecord(CliEventKinds.InvocationStarted, inv.Timestamp, writer =>
                {
                    writer.WriteString("function", inv.Function);
                    writer.WriteString("invocation_id", inv.InvocationId);
                    if (!string.IsNullOrEmpty(inv.TraceId))
                    {
                        writer.WriteString("trace_id", inv.TraceId);
                    }

                    WriteAttributes(writer, inv.Attributes, "attributes");
                });
                break;

            case InvocationCompletedEvent inv:
                WriteRecord(CliEventKinds.InvocationCompleted, inv.Timestamp, writer =>
                {
                    writer.WriteString("function", inv.Function);
                    writer.WriteString("invocation_id", inv.InvocationId);
                    if (!string.IsNullOrEmpty(inv.TraceId))
                    {
                        writer.WriteString("trace_id", inv.TraceId);
                    }

                    writer.WriteString("result", inv.Result);
                    if (inv.DurationMs is { } d)
                    {
                        writer.WriteNumber("duration_ms", Math.Round(d, 3));
                    }

                    if (!string.IsNullOrEmpty(inv.ErrorType) || !string.IsNullOrEmpty(inv.ErrorMessage))
                    {
                        writer.WriteStartObject("error");
                        if (!string.IsNullOrEmpty(inv.ErrorType))
                        {
                            writer.WriteString("type", inv.ErrorType);
                        }

                        if (!string.IsNullOrEmpty(inv.ErrorMessage))
                        {
                            writer.WriteString("message", inv.ErrorMessage);
                        }

                        writer.WriteEndObject();
                    }
                });
                break;

            case CliDiagnosticEvent diag:
                WriteRecord(CliEventKinds.CliDiagnostic, diag.Timestamp, writer =>
                {
                    writer.WriteString("code", diag.Code);
                    writer.WriteString("message", diag.Message);
                    if (!string.IsNullOrEmpty(diag.Recommendation))
                    {
                        writer.WriteString("recommendation", diag.Recommendation);
                    }
                });
                break;
        }
    }

    private void WriteRecord(string kind, DateTimeOffset timestamp, Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>(512);
        using (var writer = new Utf8JsonWriter(buffer, _writerOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", SchemaVersion);
            writer.WriteString("kind", kind);
            writer.WriteString("timestamp", timestamp.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            body(writer);
            writer.WriteEndObject();
        }

        lock (_lock)
        {
            _stdout.Write(buffer.WrittenSpan);
            _stdout.Write("\n"u8);
        }
    }

    private static void WriteAttributes(Utf8JsonWriter writer, IReadOnlyDictionary<string, object?> attributes, string propertyName = "attributes")
    {
        if (attributes.Count == 0)
        {
            return;
        }

        writer.WriteStartObject(propertyName);
        foreach (KeyValuePair<string, object?> kvp in attributes)
        {
            // Skip the discriminator — it's already surfaced as `kind`.
            if (kvp.Key == HostLogAttributeKeys.CliEventKind)
            {
                continue;
            }

            WriteValue(writer, kvp.Key, kvp.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, string name, object? value)
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
            case int i:
                writer.WriteNumber(name, i);
                break;
            case long l:
                writer.WriteNumber(name, l);
                break;
            case double d:
                writer.WriteNumber(name, d);
                break;
            case float f:
                writer.WriteNumber(name, f);
                break;
            case decimal m:
                writer.WriteNumber(name, m);
                break;
            case DateTimeOffset dto:
                writer.WriteString(name, dto.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                break;
            case string[] arr:
                writer.WriteStartArray(name);
                foreach (string s in arr)
                {
                    writer.WriteStringValue(s);
                }

                writer.WriteEndArray();
                break;
            default:
                writer.WriteString(name, value.ToString() ?? string.Empty);
                break;
        }
    }

    private static void WriteException(Utf8JsonWriter writer, string propertyName, Exception ex)
    {
        writer.WriteStartObject(propertyName);
        writer.WriteString("type", ex.GetType().FullName ?? ex.GetType().Name);
        writer.WriteString("message", ex.Message);
        if (!string.IsNullOrEmpty(ex.StackTrace))
        {
            writer.WriteString("stack", ex.StackTrace);
        }

        writer.WriteEndObject();
    }

    private void Flush()
    {
        lock (_lock)
        {
            _stdout.Flush();
        }
    }

    private static string LevelToString(LogLevel level) => level switch
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
}
