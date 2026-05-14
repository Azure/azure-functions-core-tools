// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Hosting.Dashboard;

/// <summary>
/// Mirrors dashboard events to a plain text log file.
/// </summary>
internal sealed class DashboardLogFileSink(TextWriter writer) : IDashboardEventSink
{
    private readonly TextWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    public static DashboardLogFileSink Create(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };

        return new DashboardLogFileSink(writer);
    }

    public async Task OnEventAsync(HostLogEntry entry, IReadOnlyList<DashboardEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(events);

        await _writer.WriteLineAsync(FormatEntry(entry));
        foreach (DashboardEvent dashboardEvent in events)
        {
            await _writer.WriteLineAsync(FormatEvent(dashboardEvent));
        }

        await _writer.FlushAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
    }

    private static string FormatEntry(HostLogEntry entry)
    {
        string exception = entry.Exception is null
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $" exception=\"{entry.Exception.GetType().FullName}: {entry.Exception.Message}\"");

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{entry.Timestamp:O} [{FormatLevel(entry.Level)}] {entry.Category}: {entry.Message}{exception}");
    }

    private static string FormatEvent(DashboardEvent dashboardEvent) => dashboardEvent switch
    {
        HostStateChangedEvent ev => string.Create(
            CultureInfo.InvariantCulture,
            $"{ev.Timestamp:O} [event] host_state_changed {ev.From} -> {ev.To}"),
        FunctionDiscoveredEvent ev => string.Create(
            CultureInfo.InvariantCulture,
            $"{ev.Timestamp:O} [event] function_discovered {ev.Function.Name} {ev.Function.TriggerType} {ev.Function.Route}"),
        FunctionRemovedEvent ev => string.Create(
            CultureInfo.InvariantCulture,
            $"{ev.Timestamp:O} [event] function_removed {ev.Name}"),
        InvocationStartedEvent ev => string.Create(
            CultureInfo.InvariantCulture,
            $"{ev.Timestamp:O} [event] invocation_started {ev.Function} {ev.InvocationId}"),
        InvocationCompletedEvent ev => string.Create(
            CultureInfo.InvariantCulture,
            $"{ev.Timestamp:O} [event] invocation_completed {ev.Function} {ev.InvocationId} {ev.Result} duration_ms={ev.DurationMs?.ToString(CultureInfo.InvariantCulture) ?? "-"}"),
        CliDiagnosticEvent ev => string.Create(
            CultureInfo.InvariantCulture,
            $"{ev.Timestamp:O} [event] cli_diagnostic {ev.Code}: {ev.Message}"),
        SummaryEvent ev => string.Create(
            CultureInfo.InvariantCulture,
            $"{ev.Timestamp:O} [event] summary {ev.ExitReason} functions={ev.FunctionCount} invocations={ev.TotalInvocations} errors={ev.ErrorCount}"),
        _ => string.Create(
            CultureInfo.InvariantCulture,
            $"{dashboardEvent.Timestamp:O} [event] {dashboardEvent.GetType().Name}"),
    };

    private static string FormatLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "error",
        LogLevel.Critical => "critical",
        LogLevel.None => "none",
        _ => level.ToString(),
    };
}
