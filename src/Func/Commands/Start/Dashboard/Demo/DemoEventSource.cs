// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Hosting.Dashboard.Demo;

/// <summary>
/// Scripted <see cref="IHostEventStream"/> that replays the demo scenario
/// from the design plan mockups, then expands the function set and runs a
/// long procedural burst of invocations so the UX can be evaluated under
/// realistic-volume traffic. Used as the data source for the v1 prototype
/// until real host integration lands.
/// </summary>
internal sealed class DemoEventSource(TimeProvider? timeProvider = null) : IHostEventStream
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Compresses the scripted delays by this factor. 1.0 plays at "real" pace;
    /// the orchestrator uses 0.25 so the demo finishes in a few seconds without
    /// looking unnaturally fast. Overridable via the <c>FUNC_DEMO_SPEED</c>
    /// environment variable (interpreted as a real-time multiplier, so a value
    /// of <c>0.25</c> matches the default and <c>0.1</c> is faster still).
    /// </summary>
    public double SpeedMultiplier { get; init; } = 0.25;

    /// <summary>
    /// When <c>true</c>, the source completes the stream as soon as the
    /// scripted timeline finishes instead of keeping it open for steady-state
    /// observation. Useful for smoke tests and CI. Toggle from the
    /// environment with <c>FUNC_DEMO_AUTOEXIT=1</c>.
    /// </summary>
    public bool AutoExit { get; init; }

    /// <summary>
    /// Number of functions to discover during the demo. Defaults to <c>5</c>
    /// — the canonical roster from the design plan, which keeps the
    /// compact dashboard on its full-table layout (≤8 functions). Set higher
    /// to demo the &gt;8 status-strip layout: any value above 5 adds extra
    /// HTTP functions with sequential names (<c>HttpExtra1</c>, …) and
    /// routes (<c>/api/extra-1</c>, …). Values below 5 are clamped to 5
    /// because the scripted opener unconditionally discovers the original
    /// 3 (HttpTrigger1, QueueProcessor, TimerCleanup) plus the expansion
    /// pair (HttpTriggerOrders, BlobIngest). Overridable via the
    /// <c>FUNC_DEMO_FUNCTIONS</c> environment variable or the hidden
    /// <c>--demo-functions</c> CLI option.
    /// </summary>
    public int FunctionCount { get; init; } = 5;

    /// <summary>
    /// Number of invocations to generate in the procedural burst phase that
    /// follows the scripted opening. With the 5-function demo roster this
    /// default produces roughly 200-260 records (≈3-4 records per invocation)
    /// which is enough to exercise the compact-renderer log tail, palette
    /// diversity, and error accumulation without dragging on. Override to
    /// tune the demo length.
    /// </summary>
    public int BurstInvocationCount { get; init; } = 60;

    public async IAsyncEnumerable<HostLogEntry> ReadAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int functionCount = Math.Max(BaselineFunctionCount, FunctionCount);
        (string Name, string Trigger, string? Route, string[] Methods)[] extras = BuildExtraHttpFunctions(functionCount - BaselineFunctionCount);

        foreach ((TimeSpan delay, HostLogEntry entry) in BuildTimeline(BurstInvocationCount, extras))
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(Scale(delay), _time, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            yield return entry with { Timestamp = _time.GetUtcNow() };
        }

        if (AutoExit)
        {
            yield break;
        }

        // Keep the stream open so the renderer continues to show the steady
        // state until the user cancels (or the host process exits).
        var indefinite = new TaskCompletionSource();
        using CancellationTokenRegistration reg = cancellationToken.Register(() => indefinite.TrySetResult());
        await indefinite.Task;
    }

    private TimeSpan Scale(TimeSpan ts) => TimeSpan.FromMilliseconds(ts.TotalMilliseconds * SpeedMultiplier);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static IEnumerable<(TimeSpan Delay, HostLogEntry Entry)> BuildTimeline(
        int burstCount,
        (string Name, string Trigger, string? Route, string[] Methods)[] extras)
    {
        foreach ((TimeSpan, HostLogEntry) ev in BuildOpening())
        {
            yield return ev;
        }

        foreach ((TimeSpan, HostLogEntry) ev in BuildExpansion())
        {
            yield return ev;
        }

        foreach ((TimeSpan, HostLogEntry) ev in BuildExtras(extras))
        {
            yield return ev;
        }

        // Burst draws from the original HTTP roster plus any generated
        // extras so larger function-count demos exercise the new functions
        // with real invocation traffic — otherwise extras would sit at
        // "Ready" forever and the status strip wouldn't move.
        (string Name, string Trigger, string? Route, string[] Methods)[] httpPool = [.. _httpFunctions, .. extras];

        foreach ((TimeSpan, HostLogEntry) ev in BuildBurst(burstCount, httpPool, extras))
        {
            yield return ev;
        }
    }

    /// <summary>
    /// Total number of functions discovered by the scripted opener and
    /// expansion combined: HttpTrigger1, QueueProcessor, TimerCleanup,
    /// HttpTriggerOrders, BlobIngest.
    /// </summary>
    private const int BaselineFunctionCount = 5;

    private static (string Name, string Trigger, string? Route, string[] Methods)[] BuildExtraHttpFunctions(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        var extras = new (string Name, string Trigger, string? Route, string[] Methods)[count];
        for (int i = 0; i < count; i++)
        {
            int n = i + 1;
            extras[i] = (
                Name: string.Create(System.Globalization.CultureInfo.InvariantCulture, $"HttpExtra{n}"),
                Trigger: "http",
                Route: string.Create(System.Globalization.CultureInfo.InvariantCulture, $"/api/extra-{n}"),
                Methods: ["GET"]);
        }

        return extras;
    }

    private static IEnumerable<(TimeSpan Delay, HostLogEntry Entry)> BuildExtras(
        (string Name, string Trigger, string? Route, string[] Methods)[] extras)
    {
        foreach ((string Name, string Trigger, string? Route, string[] Methods) fn in extras)
        {
            yield return (TimeSpan.FromMilliseconds(30), MakeFunctionDiscovered(fn.Name, fn.Trigger, fn.Route, fn.Methods));
        }
    }

    /// <summary>
    /// The canonical scripted opener that matches the design plan mockups:
    /// host start, three functions discovered, one HTTP success, one HTTP
    /// failure with an <see cref="IOException"/>, a file-triggered recycle,
    /// and re-discovery of the initial three functions.
    /// </summary>
    private static IEnumerable<(TimeSpan Delay, HostLogEntry Entry)> BuildOpening()
    {
        yield return (TimeSpan.Zero, MakeHostEvent("starting", null, "Host starting…", attrs: new()
        {
            [HostLogAttributeKeys.HostVersion] = "4.834.0",
            [HostLogAttributeKeys.HostListenUri] = "http://localhost:7071",
        }));

        yield return (TimeSpan.FromMilliseconds(1200), MakeHostEvent("ready", durationMs: 1241, message: "Host ready · 3 functions loaded (1.2s)"));

        yield return (TimeSpan.FromMilliseconds(50), MakeFunctionDiscovered(
            name: "HttpTrigger1",
            triggerType: "http",
            route: "/api/hello",
            methods: ["GET", "POST"]));

        yield return (TimeSpan.FromMilliseconds(20), MakeFunctionDiscovered(
            name: "QueueProcessor",
            triggerType: "queue",
            route: "my-queue",
            methods: []));

        yield return (TimeSpan.FromMilliseconds(20), MakeFunctionDiscovered(
            name: "TimerCleanup",
            triggerType: "timer",
            route: "0 */5 * * * *",
            methods: []));

        // First invocation: HttpTrigger1 GET succeeds.
        const string Id1 = "8f2a1c92-3d04-4e1f-9a55-2e4f7a9e8b01";
        const string Trace1 = "4a0c1d6e6f5a9b3c2d1e0f6a7b8c9d0e";
        yield return (TimeSpan.FromSeconds(2), MakeInvocationStarted("HttpTrigger1", Id1, Trace1, "GET", "/api/hello"));
        yield return (TimeSpan.FromMilliseconds(10), MakeLog(
            "Function.HttpTrigger1",
            LogLevel.Information,
            $"Executing 'HttpTrigger1' (Reason='HTTP', Id={Id1})",
            new()
            {
                [HostLogAttributeKeys.FunctionName] = "HttpTrigger1",
                [HostLogAttributeKeys.FunctionInvocationId] = Id1,
                [HostLogAttributeKeys.TraceId] = Trace1,
            }));
        yield return (TimeSpan.FromMilliseconds(12), MakeInvocationCompleted("HttpTrigger1", Id1, Trace1, "succeeded", 12));

        // Second invocation: HttpTrigger1 POST fails.
        const string Id2 = "2b441e84-9b21-4cf3-b6e8-8a1d3c4e5f60";
        const string Trace2 = "a3f5e9d8c7b6a5948372615049382716";
        yield return (TimeSpan.FromSeconds(15), MakeInvocationStarted("HttpTrigger1", Id2, Trace2, "POST", "/api/hello"));
        var ex = new System.IO.IOException("Access denied");
        yield return (TimeSpan.FromMilliseconds(38), new HostLogEntry(
            DateTimeOffset.UtcNow,
            "Function.HttpTrigger1",
            LogLevel.Error,
            new EventId(3, "FunctionFailed"),
            $"Executed 'HttpTrigger1' (Failed, Id={Id2})",
            ex,
            new Dictionary<string, object?>
            {
                [HostLogAttributeKeys.FunctionName] = "HttpTrigger1",
                [HostLogAttributeKeys.FunctionInvocationId] = Id2,
                [HostLogAttributeKeys.TraceId] = Trace2,
            }));
        yield return (TimeSpan.FromMilliseconds(2), MakeInvocationCompleted(
            "HttpTrigger1", Id2, Trace2, "failed", 38, errorType: "System.IO.IOException", errorMessage: "Access denied", exception: ex));

        // Host recycles on file change.
        yield return (TimeSpan.FromSeconds(14), MakeHostEvent(
            state: "recycling",
            durationMs: null,
            message: "Recycling (file changed: HttpTrigger1.cs)",
            attrs: new()
            {
                [HostLogAttributeKeys.HostRecycleReason] = "file_changed",
                [HostLogAttributeKeys.HostRecycleTrigger] = "HttpTrigger1.cs",
            }));

        yield return (TimeSpan.FromMilliseconds(1900), MakeHostEvent("ready", durationMs: 1898, message: "Host ready (1.9s)"));
        yield return (TimeSpan.FromMilliseconds(40), MakeFunctionDiscovered("HttpTrigger1", "http", "/api/hello", ["GET", "POST"]));
        yield return (TimeSpan.FromMilliseconds(20), MakeFunctionDiscovered("QueueProcessor", "queue", "my-queue", []));
        yield return (TimeSpan.FromMilliseconds(20), MakeFunctionDiscovered("TimerCleanup", "timer", "0 */5 * * * *", []));
    }

    /// <summary>
    /// Discovers the rest of a realistic-looking function set so the
    /// dashboard exits the &lt;=8 full-table layout and shows the status
    /// strip (and, once implemented, the priority-truncated header).
    /// </summary>
    private static IEnumerable<(TimeSpan Delay, HostLogEntry Entry)> BuildExpansion()
    {
        foreach ((string Name, string Trigger, string? Route, string[] Methods) fn in _expansionFunctions)
        {
            yield return (TimeSpan.FromMilliseconds(80), MakeFunctionDiscovered(fn.Name, fn.Trigger, fn.Route, fn.Methods));
        }
    }

    /// <summary>
    /// A procedurally-generated burst of mixed-trigger invocations. Uses a
    /// deterministic seed so the demo replays identically every run.
    /// Includes:
    /// <list type="bullet">
    ///   <item>HTTP requests with realistic verbs / routes / durations,</item>
    ///   <item>Queue, blob, event-hub, cosmos, and service-bus invocations,</item>
    ///   <item>Periodic timer firings (≈ every 5 scripted seconds),</item>
    ///   <item>Sprinkled user log lines, occasional warnings,</item>
    ///   <item>A ≈7% failure rate that produces varied exception types,</item>
    ///   <item>A mid-burst host recycle to exercise the wipe → repopulate path.</item>
    /// </list>
    /// </summary>
    private static IEnumerable<(TimeSpan Delay, HostLogEntry Entry)> BuildBurst(
        int invocationCount,
        (string Name, string Trigger, string? Route, string[] Methods)[] httpPool,
        (string Name, string Trigger, string? Route, string[] Methods)[] extras)
    {
        var rng = new Random(20251112);
        TimeSpan sinceLastTimer = TimeSpan.Zero;
        bool midBurstRecycleEmitted = false;
        int recycleAt = invocationCount / 2;

        for (int i = 0; i < invocationCount; i++)
        {
            // Periodic TimerHeartbeat every ~5 scripted seconds. We bias to
            // emit it before the next regular invocation when the budget is
            // due, so the cadence is visible even at low speed multipliers.
            sinceLastTimer += TimeSpan.FromMilliseconds(rng.Next(180, 320));
            if (sinceLastTimer >= TimeSpan.FromSeconds(5))
            {
                sinceLastTimer = TimeSpan.Zero;
                foreach ((TimeSpan, HostLogEntry) ev in EmitTimerHeartbeat(rng))
                {
                    yield return ev;
                }
            }

            (string Name, string Trigger, string? Route, string[] Methods) fn = PickInvocationFunction(rng, httpPool);
            string invocationId = NewGuid(rng);
            string traceId = NewTraceId(rng);
            bool fail = rng.NextDouble() < 0.07;

            (string Verb, string Target)? http = fn.Trigger == "http"
                ? PickHttpDetails(fn, rng)
                : null;

            var beforeStart = TimeSpan.FromMilliseconds(rng.Next(40, 380));

            yield return (beforeStart, MakeInvocationStartedFor(fn, invocationId, traceId, http));

            // The classic Functions "Executing 'X' (Id=…)" envelope.
            yield return (TimeSpan.FromMilliseconds(rng.Next(2, 8)), MakeLog(
                $"Function.{fn.Name}",
                LogLevel.Information,
                $"Executing '{fn.Name}' (Reason='{TriggerReason(fn.Trigger)}', Id={invocationId})",
                new()
                {
                    [HostLogAttributeKeys.FunctionName] = fn.Name,
                    [HostLogAttributeKeys.FunctionInvocationId] = invocationId,
                    [HostLogAttributeKeys.TraceId] = traceId,
                }));

            // Optional user log line — ~50% of invocations get one or two.
            int userLogCount = rng.NextDouble() < 0.5 ? rng.Next(1, 3) : 0;
            for (int u = 0; u < userLogCount; u++)
            {
                yield return (TimeSpan.FromMilliseconds(rng.Next(1, 12)), MakeUserLog(fn, invocationId, traceId, rng));
            }

            double durationMs = fail
                ? rng.Next(20, 250)
                : TriggerDurationMs(fn.Trigger, rng);

            if (fail)
            {
                (string Type, string Message, Exception Exception) error = PickError(rng);
                yield return (TimeSpan.FromMilliseconds(durationMs), new HostLogEntry(
                    DateTimeOffset.UtcNow,
                    $"Function.{fn.Name}",
                    LogLevel.Error,
                    new EventId(3, "FunctionFailed"),
                    $"Executed '{fn.Name}' (Failed, Id={invocationId})",
                    error.Exception,
                    new Dictionary<string, object?>
                    {
                        [HostLogAttributeKeys.FunctionName] = fn.Name,
                        [HostLogAttributeKeys.FunctionInvocationId] = invocationId,
                        [HostLogAttributeKeys.TraceId] = traceId,
                    }));
                yield return (TimeSpan.FromMilliseconds(1), MakeInvocationCompleted(
                    fn.Name, invocationId, traceId, "failed", durationMs, error.Type, error.Message, error.Exception));
            }
            else
            {
                yield return (TimeSpan.FromMilliseconds(durationMs), MakeInvocationCompleted(
                    fn.Name, invocationId, traceId, "succeeded", durationMs));
            }

            // Sporadic warning that isn't tied to an invocation.
            if (rng.NextDouble() < 0.04)
            {
                yield return (TimeSpan.FromMilliseconds(rng.Next(30, 90)), MakeOrphanWarning(rng));
            }

            // Mid-burst recycle to exercise the host wipe → repopulate path
            // while plenty of invocations are flying around.
            if (!midBurstRecycleEmitted && i == recycleAt)
            {
                midBurstRecycleEmitted = true;
                foreach ((TimeSpan, HostLogEntry) ev in EmitMidBurstRecycle(extras))
                {
                    yield return ev;
                }
            }
        }
    }

    private static IEnumerable<(TimeSpan, HostLogEntry)> EmitTimerHeartbeat(Random rng)
    {
        // The heartbeat reuses the already-discovered TimerCleanup function
        // so we don't inflate the function set beyond the 5-entry roster.
        const string Name = "TimerCleanup";
        string id = NewGuid(rng);
        string trace = NewTraceId(rng);
        yield return (TimeSpan.FromMilliseconds(50), MakeInvocationStartedFor(
            (Name, "timer", "0 */5 * * * *", []), id, trace, http: null));
        yield return (TimeSpan.FromMilliseconds(rng.Next(2, 6)), MakeLog(
            $"Function.{Name}",
            LogLevel.Information,
            $"Executing '{Name}' (Reason='Timer', Id={id})",
            new()
            {
                [HostLogAttributeKeys.FunctionName] = Name,
                [HostLogAttributeKeys.FunctionInvocationId] = id,
                [HostLogAttributeKeys.TraceId] = trace,
            }));
        double dur = rng.Next(2, 9);
        yield return (TimeSpan.FromMilliseconds(dur), MakeInvocationCompleted(
            Name, id, trace, "succeeded", dur));
    }

    private static IEnumerable<(TimeSpan, HostLogEntry)> EmitMidBurstRecycle(
        (string Name, string Trigger, string? Route, string[] Methods)[] extras)
    {
        yield return (TimeSpan.FromMilliseconds(120), MakeHostEvent(
            state: "recycling",
            durationMs: null,
            message: "Recycling (config changed: host.json)",
            attrs: new()
            {
                [HostLogAttributeKeys.HostRecycleReason] = "config_changed",
                [HostLogAttributeKeys.HostRecycleTrigger] = "host.json",
            }));
        yield return (TimeSpan.FromMilliseconds(900), MakeHostEvent("ready", durationMs: 924, message: "Host ready (0.9s)"));

        // Repopulate the function set after the recycle. We re-emit both
        // the original 3 from the opener and the expansion set so the
        // dashboard reflects the steady-state table again.
        yield return (TimeSpan.FromMilliseconds(30), MakeFunctionDiscovered("HttpTrigger1", "http", "/api/hello", ["GET", "POST"]));
        yield return (TimeSpan.FromMilliseconds(15), MakeFunctionDiscovered("QueueProcessor", "queue", "my-queue", []));
        yield return (TimeSpan.FromMilliseconds(15), MakeFunctionDiscovered("TimerCleanup", "timer", "0 */5 * * * *", []));
        foreach ((string Name, string Trigger, string? Route, string[] Methods) fn in _expansionFunctions)
        {
            yield return (TimeSpan.FromMilliseconds(15), MakeFunctionDiscovered(fn.Name, fn.Trigger, fn.Route, fn.Methods));
        }

        // Also re-discover any caller-supplied extras so the recycled
        // function set matches what was loaded before the wipe.
        foreach ((string Name, string Trigger, string? Route, string[] Methods) fn in extras)
        {
            yield return (TimeSpan.FromMilliseconds(15), MakeFunctionDiscovered(fn.Name, fn.Trigger, fn.Route, fn.Methods));
        }
    }

    private static (string Name, string Trigger, string? Route, string[] Methods) PickInvocationFunction(
        Random rng,
        (string Name, string Trigger, string? Route, string[] Methods)[] httpPool)
    {
        // Weighted pick across the live roster. HTTP is the most common
        // traffic source in a real project, followed by queues and blobs.
        // Timer fires occasionally on its scheduled cadence. The HTTP pool
        // is supplied by the caller so generated extras participate in the
        // burst.
        double roll = rng.NextDouble();
        if (roll < 0.55)
        {
            return httpPool[rng.Next(httpPool.Length)];
        }

        if (roll < 0.80)
        {
            return _queueFunctions[rng.Next(_queueFunctions.Length)];
        }

        if (roll < 0.95)
        {
            return _blobFunctions[rng.Next(_blobFunctions.Length)];
        }

        return ("TimerCleanup", "timer", "0 */5 * * * *", []);
    }

    private static (string Verb, string Target) PickHttpDetails(
        (string Name, string Trigger, string? Route, string[] Methods) fn,
        Random rng)
    {
        string verb = fn.Methods.Length > 0
            ? fn.Methods[rng.Next(fn.Methods.Length)]
            : "GET";
        string target = fn.Route ?? "/";

        // Light variation on the path so consecutive invocations look distinct.
        if (target.EndsWith("users", StringComparison.Ordinal))
        {
            target = rng.Next(4) == 0 ? target + "/" + rng.Next(1, 5000) : target;
        }
        else if (target.EndsWith("orders", StringComparison.Ordinal) && verb == "GET")
        {
            target = target + "/" + Guid.NewGuid().ToString("N")[..6];
        }

        return (verb, target);
    }

    private static double TriggerDurationMs(string trigger, Random rng) => trigger switch
    {
        "http" => rng.Next(500, 3000),
        "queue" => rng.Next(8, 140),
        "blob" => rng.Next(40, 320),
        "eventhub" or "cosmos" or "servicebus" => rng.Next(4, 60),
        "timer" => rng.Next(2, 9),
        _ => rng.Next(5, 60),
    };

    private static string TriggerReason(string trigger) => trigger switch
    {
        "http" => "HTTP",
        "queue" => "QueueTrigger",
        "blob" => "BlobTrigger",
        "timer" => "Timer",
        "eventhub" => "EventHubTrigger",
        "servicebus" => "ServiceBusTrigger",
        "cosmos" => "CosmosDBTrigger",
        _ => "AutomaticTrigger",
    };

    private static HostLogEntry MakeInvocationStartedFor(
        (string Name, string Trigger, string? Route, string[] Methods) fn,
        string invocationId,
        string traceId,
        (string Verb, string Target)? http)
    {
        if (http is { } h)
        {
            return MakeInvocationStarted(fn.Name, invocationId, traceId, h.Verb, h.Target);
        }

        var attrs = new Dictionary<string, object?>
        {
            [HostLogAttributeKeys.CliEventKind] = CliEventKinds.InvocationStarted,
            [HostLogAttributeKeys.FunctionName] = fn.Name,
            [HostLogAttributeKeys.FunctionInvocationId] = invocationId,
            [HostLogAttributeKeys.TraceId] = traceId,
        };

        string message = fn.Trigger switch
        {
            "queue" => $"Queue message → {fn.Route}",
            "blob" => $"Blob added → {fn.Route}",
            "timer" => $"Timer fired ({fn.Name})",
            "eventhub" => $"EventHub batch → {fn.Route}",
            "servicebus" => $"ServiceBus message → {fn.Route}",
            "cosmos" => $"Cosmos change → {fn.Route}",
            _ => fn.Name,
        };

        return new HostLogEntry(
            DateTimeOffset.UtcNow,
            $"Function.{fn.Name}",
            LogLevel.Information,
            new EventId(20, "InvocationStarted"),
            message,
            Exception: null,
            attrs);
    }

    private static HostLogEntry MakeUserLog(
        (string Name, string Trigger, string? Route, string[] Methods) fn,
        string invocationId,
        string traceId,
        Random rng)
    {
        string template = _userLogTemplates[rng.Next(_userLogTemplates.Length)];
        string filled = template
            .Replace("{n}", rng.Next(1, 9999).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{ms}", rng.Next(2, 250).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{guid6}", Guid.NewGuid().ToString("N")[..6], StringComparison.Ordinal);

        return MakeLog(
            $"Function.{fn.Name}",
            LogLevel.Information,
            filled,
            new()
            {
                [HostLogAttributeKeys.FunctionName] = fn.Name,
                [HostLogAttributeKeys.FunctionInvocationId] = invocationId,
                [HostLogAttributeKeys.TraceId] = traceId,
            });
    }

    private static HostLogEntry MakeOrphanWarning(Random rng)
    {
        string template = _orphanWarnings[rng.Next(_orphanWarnings.Length)];
        return MakeLog("Host.Workers", LogLevel.Warning, template, attrs: []);
    }

    private static (string Type, string Message, Exception Exception) PickError(Random rng)
    {
        int slot = rng.Next(_errorBuilders.Length);
        return _errorBuilders[slot]();
    }

    private static string NewGuid(Random rng)
    {
        Span<byte> bytes = stackalloc byte[16];
        rng.NextBytes(bytes);
        return new Guid(bytes).ToString();
    }

    private static string NewTraceId(Random rng)
    {
        Span<byte> bytes = stackalloc byte[16];
        rng.NextBytes(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // 5-function demo roster: the 3 originals from BuildOpening
    // (HttpTrigger1, QueueProcessor, TimerCleanup) plus the 2 below. This
    // keeps the dashboard on the full-table layout path (≤8 functions).
    private static readonly (string Name, string Trigger, string? Route, string[] Methods)[] _expansionFunctions =
    [
        ("HttpTriggerOrders", "http", "/api/orders", ["GET", "POST"]),
        ("BlobIngest", "blob", "uploads/{name}", []),
    ];

    private static readonly (string Name, string Trigger, string? Route, string[] Methods)[] _httpFunctions =
    [
        ("HttpTrigger1", "http", "/api/hello", ["GET", "POST"]),
        ("HttpTriggerOrders", "http", "/api/orders", ["GET", "POST"]),
    ];

    private static readonly (string Name, string Trigger, string? Route, string[] Methods)[] _queueFunctions =
    [
        ("QueueProcessor", "queue", "my-queue", []),
    ];

    private static readonly (string Name, string Trigger, string? Route, string[] Methods)[] _blobFunctions =
    [
        ("BlobIngest", "blob", "uploads/{name}", []),
    ];

    private static readonly string[] _userLogTemplates =
    [
        "Cache miss for key 'order-{n}'",
        "Cache hit for key 'order-{n}'",
        "Database query took {ms}ms",
        "Sending notification to user #{n}",
        "Validated payload (size={n} bytes)",
        "Retrying upstream call (attempt {n})",
        "Fetched {n} rows from inventory",
        "Enqueued follow-up message {guid6}",
        "Skipping duplicate message {guid6}",
        "Dispatched job {guid6} to worker pool",
    ];

    private static readonly string[] _orphanWarnings =
    [
        "Slow listener heartbeat detected (lag {n}ms)".Replace("{n}", "84", StringComparison.Ordinal),
        "Worker pool saturated; spawning replacement",
        "Token cache refresh took longer than expected",
        "Connection retry succeeded after transient failure",
    ];

    private static readonly Func<(string Type, string Message, Exception Exception)>[] _errorBuilders =
    [
        () => ("System.IO.IOException", "Access denied", new System.IO.IOException("Access denied")),
        () => ("System.TimeoutException", "Operation timed out after 30s", new TimeoutException("Operation timed out after 30s")),
        () => ("System.InvalidOperationException", "Connection pool exhausted", new InvalidOperationException("Connection pool exhausted")),
        () => ("System.Net.Sockets.SocketException", "No connection could be made to upstream", new System.Net.Sockets.SocketException(10061)),
        () => ("System.Text.Json.JsonException", "Invalid payload: missing 'orderId'", new System.Text.Json.JsonException("Invalid payload: missing 'orderId'")),
        () => ("Microsoft.Azure.Cosmos.CosmosException", "Throughput limit exceeded (429)", new InvalidOperationException("Throughput limit exceeded (429)")),
    ];

    private static HostLogEntry MakeHostEvent(string state, double? durationMs, string message, Dictionary<string, object?>? attrs = null)
    {
        attrs ??= [];
        attrs[HostLogAttributeKeys.HostState] = state;
        attrs[HostLogAttributeKeys.CliEventKind] = CliEventKinds.HostStateChanged;
        if (durationMs is { } d)
        {
            attrs[HostLogAttributeKeys.HostStartupDurationMs] = d;
        }

        return new HostLogEntry(
            DateTimeOffset.UtcNow,
            "Host.Lifecycle",
            LogLevel.Information,
            new EventId(1, "HostState"),
            message,
            Exception: null,
            attrs);
    }

    private static HostLogEntry MakeFunctionDiscovered(string name, string triggerType, string? route, string[] methods)
    {
        var attrs = new Dictionary<string, object?>
        {
            [HostLogAttributeKeys.CliEventKind] = CliEventKinds.FunctionDiscovered,
            [HostLogAttributeKeys.FunctionName] = name,
            [HostLogAttributeKeys.FunctionTriggerType] = triggerType,
        };
        if (!string.IsNullOrEmpty(route))
        {
            attrs[HostLogAttributeKeys.FunctionRoute] = route;
        }

        if (methods.Length > 0)
        {
            attrs[HostLogAttributeKeys.FunctionHttpMethods] = methods;
        }

        return new HostLogEntry(
            DateTimeOffset.UtcNow,
            "Host.Indexer",
            LogLevel.Information,
            new EventId(100, "FunctionDiscovered"),
            $"Function loaded: {name}",
            Exception: null,
            attrs);
    }

    private static HostLogEntry MakeInvocationStarted(string function, string id, string traceId, string method, string target)
    {
        var attrs = new Dictionary<string, object?>
        {
            [HostLogAttributeKeys.CliEventKind] = CliEventKinds.InvocationStarted,
            [HostLogAttributeKeys.FunctionName] = function,
            [HostLogAttributeKeys.FunctionInvocationId] = id,
            [HostLogAttributeKeys.TraceId] = traceId,
            [HostLogAttributeKeys.HttpMethod] = method,
            [HostLogAttributeKeys.HttpTarget] = target,
        };

        return new HostLogEntry(
            DateTimeOffset.UtcNow,
            $"Function.{function}",
            LogLevel.Information,
            new EventId(20, "InvocationStarted"),
            $"{method} {target}",
            Exception: null,
            attrs);
    }

    private static HostLogEntry MakeInvocationCompleted(
        string function,
        string id,
        string traceId,
        string result,
        double durationMs,
        string? errorType = null,
        string? errorMessage = null,
        Exception? exception = null)
    {
        var attrs = new Dictionary<string, object?>
        {
            [HostLogAttributeKeys.CliEventKind] = CliEventKinds.InvocationCompleted,
            [HostLogAttributeKeys.FunctionName] = function,
            [HostLogAttributeKeys.FunctionInvocationId] = id,
            [HostLogAttributeKeys.TraceId] = traceId,
            [HostLogAttributeKeys.FunctionResult] = result,
            [HostLogAttributeKeys.DurationMs] = durationMs,
        };

        string message = result == "failed"
            ? $"Executed '{function}' (Failed, Id={id})"
            : $"Executed '{function}' (Succeeded, Id={id})";

        return new HostLogEntry(
            DateTimeOffset.UtcNow,
            $"Function.{function}",
            result == "failed" ? LogLevel.Error : LogLevel.Information,
            new EventId(21, "InvocationCompleted"),
            message,
            exception,
            attrs);
    }

    private static HostLogEntry MakeLog(string category, LogLevel level, string message, Dictionary<string, object?> attrs)
        => new(DateTimeOffset.UtcNow, category, level, default, message, Exception: null, attrs);
}
