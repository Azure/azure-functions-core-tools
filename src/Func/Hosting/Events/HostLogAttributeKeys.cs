// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting.Events;

/// <summary>
/// Well-known attribute keys used by CLI consumers when interpreting
/// <see cref="HostLogEntry.Attributes"/>. Aligned with OTel FaaS semantic
/// conventions where applicable so a future OTLP receiver can populate the
/// same bag without translation. These are conventions, not contract — a
/// producer may omit any of them.
/// </summary>
internal static class HostLogAttributeKeys
{
    public const string FunctionName = "function.name";

    public const string FunctionInvocationId = "function.invocation_id";

    /// <summary>
    /// <c>"succeeded"</c> or <c>"failed"</c>.
    /// </summary>
    public const string FunctionResult = "function.result";

    /// <summary>
    /// Trigger type token, e.g. <c>"http"</c>, <c>"queue"</c>, <c>"timer"</c>, <c>"blob"</c>.
    /// </summary>
    public const string FunctionTriggerType = "function.trigger_type";

    public const string FunctionRoute = "function.route";

    /// <summary>
    /// Array of HTTP verbs for HTTP-triggered functions (<c>string[]</c>).
    /// </summary>
    public const string FunctionHttpMethods = "function.http_methods";

    public const string DurationMs = "duration_ms";

    public const string TraceId = "trace_id";

    public const string SpanId = "span_id";

    public const string ParentSpanId = "parent_span_id";

    /// <summary>
    /// One of <c>"starting"</c>, <c>"ready"</c>, <c>"recycling"</c>, <c>"stopped"</c>.
    /// </summary>
    public const string HostState = "host.state";

    /// <summary>
    /// Reason for a host recycle (e.g. <c>"file_changed"</c>) — paired with
    /// <see cref="HostRecycleTrigger"/> to identify the file.
    /// </summary>
    public const string HostRecycleReason = "host.recycle_reason";

    public const string HostRecycleTrigger = "host.recycle_trigger";

    public const string HostVersion = "host.version";

    public const string HostListenUri = "host.listen_uri";

    public const string HostStartupDurationMs = "host.startup_duration_ms";

    public const string HttpMethod = "http.method";

    public const string HttpTarget = "http.target";

    public const string HttpStatusCode = "http.status_code";

    /// <summary>
    /// Optional discriminator used by the producer to label a record as a
    /// specific kind of CLI event (see <see cref="CliEventKinds"/>). When
    /// present, the consumer trusts it; when absent, the consumer falls back
    /// to attribute-presence heuristics.
    /// </summary>
    public const string CliEventKind = "cli.event_kind";
}

/// <summary>
/// Values recognised on the <see cref="HostLogAttributeKeys.CliEventKind"/>
/// attribute. Values are stable strings — they're also the discriminator
/// surfaced on JSON-mode records.
/// </summary>
internal static class CliEventKinds
{
    public const string Log = "log";

    public const string HostStateChanged = "host_state_changed";

    public const string FunctionDiscovered = "function_discovered";

    public const string FunctionRemoved = "function_removed";

    public const string InvocationStarted = "invocation_started";

    public const string InvocationCompleted = "invocation_completed";

    public const string CliDiagnostic = "cli_diagnostic";

    public const string Summary = "summary";
}
