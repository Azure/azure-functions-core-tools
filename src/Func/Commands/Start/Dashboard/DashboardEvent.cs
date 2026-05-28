// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting.Dashboard;

/// <summary>
/// Synthetic events the dashboard derives from raw <see cref="Events.HostLogEntry"/>
/// records. Renderers (especially the JSON renderer) project these into
/// their first-class output kinds (<c>invocation_started</c>,
/// <c>host_state_changed</c>, etc.).
/// </summary>
internal abstract record DashboardEvent(DateTimeOffset Timestamp);

internal sealed record HostStateChangedEvent(
    DateTimeOffset Timestamp,
    HostLifecycleState From,
    HostLifecycleState To,
    double? DurationMs,
    string? Reason,
    string? Trigger)
    : DashboardEvent(Timestamp);

internal sealed record FunctionDiscoveredEvent(DateTimeOffset Timestamp, FunctionInfo Function)
    : DashboardEvent(Timestamp);

internal sealed record FunctionRemovedEvent(DateTimeOffset Timestamp, string Name)
    : DashboardEvent(Timestamp);

internal sealed record InvocationStartedEvent(
    DateTimeOffset Timestamp,
    string Function,
    string InvocationId,
    string? TraceId,
    IReadOnlyDictionary<string, object?> Attributes)
    : DashboardEvent(Timestamp);

internal sealed record InvocationCompletedEvent(
    DateTimeOffset Timestamp,
    string Function,
    string InvocationId,
    string? TraceId,
    string Result,
    double? DurationMs,
    string? ErrorType,
    string? ErrorMessage)
    : DashboardEvent(Timestamp);

internal sealed record CliDiagnosticEvent(DateTimeOffset Timestamp, string Code, string Message, string? Recommendation)
    : DashboardEvent(Timestamp);

internal sealed record SummaryEvent(
    DateTimeOffset Timestamp,
    string ExitReason,
    double UptimeSeconds,
    int FunctionCount,
    int TotalInvocations,
    int SucceededInvocations,
    int FailedInvocations,
    int ErrorCount)
    : DashboardEvent(Timestamp);
