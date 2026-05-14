// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting.Events;

namespace Azure.Functions.Cli.Hosting.Dashboard;

/// <summary>
/// In-memory state derived from a stream of <see cref="HostLogEntry"/> records.
/// Single source of truth for the dashboard: functions table, host
/// lifecycle, invocation counters. Renderers consume <see cref="Snapshot"/>
/// values; they never mutate.
/// </summary>
/// <remarks>
/// Thread-safe via a single coarse-grained lock — observation rate is low
/// (one record per log line) and snapshot construction is cheap.
///
/// Event derivation priority (matches Q3 in the design plan):
/// <list type="number">
///   <item>Explicit <see cref="HostLogAttributeKeys.CliEventKind"/> attribute.</item>
///   <item>Attribute-presence heuristics (e.g. <c>function.invocation_id</c> + <c>function.result</c> → invocation completed).</item>
///   <item>Message-pattern fallback (not implemented in the prototype — wire on top of step 2).</item>
/// </list>
/// </remarks>
internal sealed class DashboardState
{
    private readonly object _lock = new();
    private readonly Dictionary<string, FunctionEntry> _functions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActiveInvocation> _active = new(StringComparer.Ordinal);

    private string? _hostVersion;
    private string? _listenUri;
    private HostLifecycleState _hostState = HostLifecycleState.Starting;
    private DateTimeOffset _hostStartedAt = DateTimeOffset.UtcNow;
    private DateTimeOffset? _lastHostStateChangeAt;

    private int _totalInvocations;
    private int _succeededInvocations;
    private int _failedInvocations;

    public DashboardSnapshot Snapshot()
    {
        lock (_lock)
        {
            var functions = new List<FunctionInfo>(_functions.Count);
            foreach (FunctionEntry entry in _functions.Values)
            {
                functions.Add(entry.ToInfo());
            }

            return new DashboardSnapshot(
                _hostState,
                _hostVersion,
                _listenUri,
                _hostStartedAt,
                functions,
                _active.Count,
                _totalInvocations,
                _succeededInvocations,
                _failedInvocations,
                _failedInvocations);
        }
    }

    /// <summary>
    /// Updates state from <paramref name="entry"/> and returns any synthetic
    /// dashboard events that were derived. The raw log entry is still
    /// forwarded to renderers; this method is purely about deriving
    /// higher-level signals.
    /// </summary>
    public IReadOnlyList<DashboardEvent> Observe(HostLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        List<DashboardEvent>? events = null;
        lock (_lock)
        {
            string? kind = entry.GetAttribute<string>(HostLogAttributeKeys.CliEventKind);

            // Host startup metadata is opportunistically scraped from any
            // entry that carries it, so the header populates as soon as the
            // first relevant record arrives.
            string? version = entry.GetAttribute<string>(HostLogAttributeKeys.HostVersion);
            if (!string.IsNullOrEmpty(version))
            {
                _hostVersion = version;
            }

            string? listen = entry.GetAttribute<string>(HostLogAttributeKeys.HostListenUri);
            if (!string.IsNullOrEmpty(listen))
            {
                _listenUri = listen;
            }

            if (kind == CliEventKinds.HostStateChanged || entry.Attributes.ContainsKey(HostLogAttributeKeys.HostState))
            {
                DashboardEvent? ev = ObserveHostState(entry);
                if (ev is not null)
                {
                    (events ??= []).Add(ev);
                }
            }
            else if (kind == CliEventKinds.FunctionDiscovered || IsImplicitFunctionDiscovery(entry, kind))
            {
                DashboardEvent? ev = ObserveFunctionDiscovered(entry);
                if (ev is not null)
                {
                    (events ??= []).Add(ev);
                }
            }
            else if (kind == CliEventKinds.FunctionRemoved)
            {
                DashboardEvent? ev = ObserveFunctionRemoved(entry);
                if (ev is not null)
                {
                    (events ??= []).Add(ev);
                }
            }
            else if (kind == CliEventKinds.InvocationStarted || IsImplicitInvocationStarted(entry, kind))
            {
                DashboardEvent? ev = ObserveInvocationStarted(entry);
                if (ev is not null)
                {
                    (events ??= []).Add(ev);
                }
            }
            else if (kind == CliEventKinds.InvocationCompleted || IsImplicitInvocationCompleted(entry, kind))
            {
                DashboardEvent? ev = ObserveInvocationCompleted(entry);
                if (ev is not null)
                {
                    (events ??= []).Add(ev);
                }
            }
        }

        return (IReadOnlyList<DashboardEvent>?)events ?? [];
    }

    public SummaryEvent BuildSummary(string exitReason, DateTimeOffset at)
    {
        lock (_lock)
        {
            double uptimeSeconds = (at - _hostStartedAt).TotalSeconds;
            return new SummaryEvent(
                at,
                exitReason,
                Math.Max(0, uptimeSeconds),
                _functions.Count,
                _totalInvocations,
                _succeededInvocations,
                _failedInvocations,
                _failedInvocations);
        }
    }

    private DashboardEvent? ObserveHostState(HostLogEntry entry)
    {
        string? rawState = entry.GetAttribute<string>(HostLogAttributeKeys.HostState);
        if (string.IsNullOrEmpty(rawState) || !TryParseHostState(rawState, out HostLifecycleState newState))
        {
            return null;
        }

        if (newState == _hostState)
        {
            return null;
        }

        HostLifecycleState previous = _hostState;
        _hostState = newState;

        DateTimeOffset now = entry.Timestamp;
        double? duration = entry.GetAttribute<double?>(HostLogAttributeKeys.HostStartupDurationMs);
        if (duration is null && _lastHostStateChangeAt is { } last)
        {
            duration = (now - last).TotalMilliseconds;
        }

        _lastHostStateChangeAt = now;

        if (previous == HostLifecycleState.Recycling && newState == HostLifecycleState.Ready)
        {
            // Drop stale function set; the next discovery records repopulate.
            _functions.Clear();
            _active.Clear();
        }

        if (newState == HostLifecycleState.Ready && _hostStartedAt == default)
        {
            _hostStartedAt = now;
        }

        string? reason = entry.GetAttribute<string>(HostLogAttributeKeys.HostRecycleReason);
        string? trigger = entry.GetAttribute<string>(HostLogAttributeKeys.HostRecycleTrigger);

        return new HostStateChangedEvent(now, previous, newState, duration, reason, trigger);
    }

    private DashboardEvent? ObserveFunctionDiscovered(HostLogEntry entry)
    {
        string? name = entry.GetAttribute<string>(HostLogAttributeKeys.FunctionName);
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        string triggerType = entry.GetAttribute<string>(HostLogAttributeKeys.FunctionTriggerType) ?? "unknown";
        string? route = entry.GetAttribute<string>(HostLogAttributeKeys.FunctionRoute);
        IReadOnlyList<string> methods = entry.GetAttribute<string[]>(HostLogAttributeKeys.FunctionHttpMethods) is { } arr
            ? arr
            : [];

        if (_functions.TryGetValue(name, out FunctionEntry? existing))
        {
            existing.TriggerType = triggerType;
            existing.Route = route;
            existing.HttpMethods = methods;
            return null;
        }

        var fresh = new FunctionEntry
        {
            Name = name,
            TriggerType = triggerType,
            Route = route,
            HttpMethods = methods,
        };
        _functions[name] = fresh;

        return new FunctionDiscoveredEvent(entry.Timestamp, fresh.ToInfo());
    }

    private DashboardEvent? ObserveFunctionRemoved(HostLogEntry entry)
    {
        string? name = entry.GetAttribute<string>(HostLogAttributeKeys.FunctionName);
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (!_functions.Remove(name))
        {
            return null;
        }

        return new FunctionRemovedEvent(entry.Timestamp, name);
    }

    private DashboardEvent? ObserveInvocationStarted(HostLogEntry entry)
    {
        string? name = entry.GetAttribute<string>(HostLogAttributeKeys.FunctionName);
        string? id = entry.GetAttribute<string>(HostLogAttributeKeys.FunctionInvocationId);
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id))
        {
            return null;
        }

        if (_active.ContainsKey(id))
        {
            return null;
        }

        string? trace = entry.GetAttribute<string>(HostLogAttributeKeys.TraceId);
        _active[id] = new ActiveInvocation(name, entry.Timestamp);

        if (_functions.TryGetValue(name, out FunctionEntry? f))
        {
            f.ActiveInvocations++;
            f.Status = FunctionStatus.Active;
        }

        return new InvocationStartedEvent(entry.Timestamp, name, id, trace, entry.Attributes);
    }

    private DashboardEvent? ObserveInvocationCompleted(HostLogEntry entry)
    {
        string? name = entry.GetAttribute<string>(HostLogAttributeKeys.FunctionName);
        string? id = entry.GetAttribute<string>(HostLogAttributeKeys.FunctionInvocationId);
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id))
        {
            return null;
        }

        string result = entry.GetAttribute<string>(HostLogAttributeKeys.FunctionResult) ?? "succeeded";

        double? duration = entry.GetAttribute<double?>(HostLogAttributeKeys.DurationMs);
        if (duration is null && _active.TryGetValue(id, out ActiveInvocation invo))
        {
            duration = (entry.Timestamp - invo.StartedAt).TotalMilliseconds;
        }

        _active.Remove(id);

        _totalInvocations++;
        bool failed = string.Equals(result, "failed", StringComparison.OrdinalIgnoreCase);
        if (failed)
        {
            _failedInvocations++;
        }
        else
        {
            _succeededInvocations++;
        }

        string? errorType = null;
        string? errorMessage = null;

        if (_functions.TryGetValue(name, out FunctionEntry? f))
        {
            if (f.ActiveInvocations > 0)
            {
                f.ActiveInvocations--;
            }

            f.TotalInvocations++;
            f.LastInvocationAt = entry.Timestamp;

            if (failed)
            {
                f.TotalErrors++;
                f.Status = FunctionStatus.Error;
                if (entry.Exception is not null)
                {
                    errorType = entry.Exception.GetType().FullName;
                    errorMessage = entry.Exception.Message;
                    f.LastErrorMessage = errorMessage;
                }
            }
            else if (f.ActiveInvocations == 0 && f.Status != FunctionStatus.Error)
            {
                f.Status = FunctionStatus.Ready;
            }
        }

        string? trace = entry.GetAttribute<string>(HostLogAttributeKeys.TraceId);
        return new InvocationCompletedEvent(entry.Timestamp, name, id, trace, result, duration, errorType, errorMessage);
    }

    private static bool IsImplicitFunctionDiscovery(HostLogEntry entry, string? kind)
        => kind is null
           && entry.Attributes.ContainsKey(HostLogAttributeKeys.FunctionName)
           && entry.Attributes.ContainsKey(HostLogAttributeKeys.FunctionTriggerType)
           && !entry.Attributes.ContainsKey(HostLogAttributeKeys.FunctionInvocationId);

    private static bool IsImplicitInvocationStarted(HostLogEntry entry, string? kind)
        => kind is null
           && entry.Attributes.ContainsKey(HostLogAttributeKeys.FunctionInvocationId)
           && !entry.Attributes.ContainsKey(HostLogAttributeKeys.FunctionResult);

    private static bool IsImplicitInvocationCompleted(HostLogEntry entry, string? kind)
        => kind is null
           && entry.Attributes.ContainsKey(HostLogAttributeKeys.FunctionInvocationId)
           && entry.Attributes.ContainsKey(HostLogAttributeKeys.FunctionResult);

    private static bool TryParseHostState(string value, out HostLifecycleState state)
    {
        switch (value.ToLowerInvariant())
        {
            case "starting":
                state = HostLifecycleState.Starting;
                return true;
            case "ready":
                state = HostLifecycleState.Ready;
                return true;
            case "recycling":
                state = HostLifecycleState.Recycling;
                return true;
            case "stopped":
                state = HostLifecycleState.Stopped;
                return true;
            default:
                state = default;
                return false;
        }
    }

    private readonly record struct ActiveInvocation(string Function, DateTimeOffset StartedAt);

    private sealed class FunctionEntry
    {
        public required string Name { get; init; }

        public string TriggerType { get; set; } = "unknown";

        public string? Route { get; set; }

        public IReadOnlyList<string> HttpMethods { get; set; } = [];

        public FunctionStatus Status { get; set; } = FunctionStatus.Ready;

        public int ActiveInvocations { get; set; }

        public int TotalInvocations { get; set; }

        public int TotalErrors { get; set; }

        public DateTimeOffset? LastInvocationAt { get; set; }

        public string? LastErrorMessage { get; set; }

        public FunctionInfo ToInfo() => new(
            Name,
            TriggerType,
            Route,
            HttpMethods,
            Status,
            ActiveInvocations,
            TotalInvocations,
            TotalErrors,
            LastInvocationAt,
            LastErrorMessage);
    }
}
