// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Interactive TTY renderer. Combines a live-redrawn header (functions table + counters) with a streaming log tail in a single <c>Live</c>
/// region — this keeps redraws and log appends consistent without cursor-juggling.
/// </summary>
/// <remarks>
/// Prototype scope: keyboard input handling, the help overlay, the function-browser overlay, and the priority-sorted truncated header
/// variant are not yet implemented. The renderer demonstrates the live header / live log tail UX and falls back to a status strip when many
/// functions are loaded.
/// </remarks>
internal sealed class CompactRenderer(
    IInteractionService interaction,
    FunctionPalette palette,
    IAnsiConsole? console = null) : IDashboardRenderer
{
    private const int MaxLogTailLines = 200;

    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly FunctionPalette _palette = palette ?? throw new ArgumentNullException(nameof(palette));
    private readonly IAnsiConsole _console = console ?? AnsiConsole.Console;
    private readonly object _stateLock = new();
    private readonly Queue<IRenderable> _logTail = new();

    private DashboardState _state = null!;
    private CancellationTokenSource? _liveCts;
    private SemaphoreSlim? _redrawSignal;
    private Task? _liveTask;
    private int _lastKnownWidth;
    private int _lastKnownHeight;

    private ITheme Theme => _interaction.Theme;

    // Cached theme-derived markup tags. Spectre's Style.ToMarkup() returns
    // the bracket-contents form (e.g. "grey", "red bold") so we can embed
    // them in markup strings as $"[{_mutedTag}]…[/]" while still funneling
    // every color through ITheme. We snapshot once at first use so each
    // live-redraw tick doesn't re-stringify the same styles.
    private string MutedTag => field ??= Theme.Muted.ToMarkup();
    private string EmphasisTag => field ??= Theme.Emphasis.ToMarkup();
    private string SuccessTag => field ??= Theme.Success.ToMarkup();
    private string ErrorTag => field ??= Theme.Error.ToMarkup();
    private string WarningTag => field ??= Theme.Warning.ToMarkup();
    private string ActiveTag => field ??= Theme.Active.ToMarkup();
    private string HyperlinkTag => field ??= Theme.Hyperlink.ToMarkup();

    public Task OnStartAsync(DashboardState state, CancellationToken cancellationToken)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _redrawSignal = new SemaphoreSlim(initialCount: 1, maxCount: int.MaxValue);
        _liveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _console.Cursor.Hide();
        _liveTask = Task.Run(() => RunLiveLoopAsync(_liveCts.Token));
        return Task.CompletedTask;
    }

    public Task OnEventAsync(HostLogEntry entry, IReadOnlyList<DashboardEvent> events, CancellationToken cancellationToken)
    {
        if (ShouldSuppress(entry, events))
        {
            return Task.CompletedTask;
        }

        IRenderable line = FormatLogLine(entry, events);
        lock (_stateLock)
        {
            _logTail.Enqueue(line);
            while (_logTail.Count > MaxLogTailLines)
            {
                _logTail.Dequeue();
            }
        }

        SignalRedraw();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Filters out raw log lines that are already covered by a synthetic
    /// event in this batch, plus the classic WebJobs <c>Executing '...'</c> /
    /// <c>Executed '...'</c> envelope which restates information already
    /// captured by <see cref="InvocationStartedEvent"/> and
    /// <see cref="InvocationCompletedEvent"/>.
    /// </summary>
    private static bool ShouldSuppress(HostLogEntry entry, IReadOnlyList<DashboardEvent> events)
    {
        if (events.Count > 0)
        {
            // Synthetic event(s) already convey the salient info; rendering
            // the raw line on top of it would just duplicate the row.
            return false;
        }

        if (IsFunctionsInvocationEnvelope(entry.Message))
        {
            return true;
        }

        return false;
    }

    private static bool IsFunctionsInvocationEnvelope(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        return message.StartsWith("Executing '", StringComparison.Ordinal)
            || message.StartsWith("Executed '", StringComparison.Ordinal);
    }

    public async Task OnSummaryAsync(SummaryEvent summary, CancellationToken cancellationToken)
    {
        if (_liveCts is { } cts)
        {
            cts.Cancel();
        }

        if (_liveTask is { } task)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // Expected on clean shutdown.
            }
        }

        _console.Cursor.Show();

        var uptime = TimeSpan.FromSeconds(summary.UptimeSeconds);
        _interaction.WriteBlankLine();
        _interaction.WriteLine($"  Azure Functions host stopped after {uptime.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)}");
        _interaction.WriteLine($"  {summary.FunctionCount} functions  ·  {summary.TotalInvocations} invocations  ·  {summary.ErrorCount} error{(summary.ErrorCount == 1 ? string.Empty : "s")}");
    }

    public ValueTask DisposeAsync()
    {
        _liveCts?.Dispose();
        _redrawSignal?.Dispose();
        return ValueTask.CompletedTask;
    }

    private void SignalRedraw()
    {
        try
        {
            _redrawSignal?.Release();
        }
        catch (ObjectDisposedException)
        {
            // Live loop already torn down.
        }
    }

    private async Task RunLiveLoopAsync(CancellationToken cancellationToken)
    {
        // Outer loop: each iteration runs one LiveDisplay session. When a
        // terminal resize is detected we exit the inner Live region so
        // Spectre tears down its internal shape/cursor state, clear the
        // screen + scrollback, then come around and start a brand-new Live
        // region at the new dimensions. That's the only reliable way to
        // avoid stale-shape artifacts: in particular, on shrinks the new
        // content is taller than the viewport and Spectre's "move up N
        // lines to clear previous frame" math diverges from the terminal's
        // clamped cursor behavior, which leaves blank rows wedged between
        // elements on subsequent refreshes.
        while (!cancellationToken.IsCancellationRequested)
        {
            _lastKnownWidth = _console.Profile.Width;
            _lastKnownHeight = _console.Profile.Height;
            bool resizeDetected = false;

            try
            {
                await _console.Live(BuildLayout())
                    .AutoClear(false)
                    .Overflow(VerticalOverflow.Ellipsis)
                    // Cropping.Top is a safety net: BuildLayout already sizes
                    // the log tail to the viewport, but if the header gets
                    // taller than estimated (e.g. very long route strings
                    // wrap) we want the newest log lines to remain visible —
                    // losing the banner top is preferable to freezing the
                    // bottom of the log stream.
                    .Cropping(VerticalOverflowCropping.Top)
                    .StartAsync(async ctx =>
                    {
                        while (!cancellationToken.IsCancellationRequested)
                        {
                            int width = _console.Profile.Width;
                            int height = _console.Profile.Height;
                            if (width != _lastKnownWidth || height != _lastKnownHeight)
                            {
                                resizeDetected = true;
                                return;
                            }

                            ctx.UpdateTarget(BuildLayout());
                            try
                            {
                                await _redrawSignal!.WaitAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
                            }
                            catch (OperationCanceledException)
                            {
                                return;
                            }
                        }
                    });
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!resizeDetected)
            {
                // Normal shutdown path: cancellation requested, loop exits
                // without restarting. Spectre's AutoClear(false) preserves
                // the final frame so the post-stream summary lands beneath.
                return;
            }

            // Resize-restart path: tear-down complete. Wipe the viewport
            // *and* scrollback (\x1b[3J) so no ghost rows from the old
            // layout survive, then loop and reopen a fresh LiveDisplay at
            // the new dimensions. Without the scrollback clear, previous
            // frames pushed above the viewport during a shrink would still
            // be visible if the user scrolled up.
            _console.Clear(home: true);
            _console.Write(new ControlCode("\u001b[3J"));
        }
    }

    private IRenderable BuildLayout()
    {
        DashboardSnapshot snapshot = _state.Snapshot();
        IRenderable header = BuildHeader(snapshot);

        IRenderable[] tail;
        lock (_stateLock)
        {
            tail = [.. _logTail];
        }

        // Slice the log tail to what actually fits on screen so we render
        // true scrolling behavior (newest at the bottom, oldest drop off the
        // top of the visible region) instead of freezing once the viewport
        // fills. The queue itself still holds up to MaxLogTailLines for
        // future scroll-back / resize support.
        int logBudget = ComputeLogBudget(snapshot);
        if (tail.Length > logBudget)
        {
            tail = tail[(tail.Length - logBudget)..];
        }

        var rows = new Rows(
            header,
            new Rule("logs").LeftJustified().RuleStyle(Theme.Muted),
            tail.Length == 0
                ? new Markup($"[{MutedTag}]Waiting for events…[/]")
                : new Rows(tail));

        return rows;
    }

    private int ComputeLogBudget(DashboardSnapshot snapshot)
    {
        // Spectre reports sentinel values when stdout isn't a real terminal
        // (e.g., redirected to a pipe). Clamp to a sensible default so the
        // compact renderer still produces useful output if it's somehow
        // reached without the terminal-safety fallback firing.
        int viewportHeight = _console.Profile.Height;
        if (viewportHeight <= 0 || viewportHeight > 1000)
        {
            viewportHeight = 30;
        }

        // Header lines breakdown (matches BuildHeader output):
        //   bannerPanel (rounded box):              3 lines
        //   functions block (count-dependent):      see switch
        //   status line:                            1 line
        //   "logs" rule:                            1 line
        //   safety pad (resize/wrap slack):         2 lines
        int functionsLines = snapshot.Functions.Count switch
        {
            0 => 1,
            <= 8 => snapshot.Functions.Count + 1, // 1 column-header row + N data rows
            _ => 1, // status strip
        };

        int reservedForHeader = 3 + functionsLines + 1 + 1 + 2;

        // Always reserve at least 3 lines for logs so the dashboard remains
        // useful even on very small terminals (the header simply overflows).
        return Math.Max(3, viewportHeight - reservedForHeader);
    }

    private IRenderable BuildHeader(DashboardSnapshot snapshot)
    {
        string version = snapshot.HostVersion ?? "—";
        string listen = snapshot.ListenUri ?? "—";

        // No emoji in the banner: ⚡ (U+26A1) is ambiguous-width — terminals
        // render it as 2 cells while Spectre's wcwidth treats it as 1, which
        // misaligns the panel's right border by one column.
        //
        // Layout: brand + host version on the left, listen URL pinned to
        // the right. We use a borderless/headerless Table (not a Grid)
        // because Table.Expand() actually stretches to fill the parent
        // Panel's width — Grid measures to its content and would leave the
        // "right-aligned" column with no slack to align against.
        Table bannerTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .Expand()
            .AddColumn(new TableColumn(string.Empty).NoWrap().PadLeft(0).PadRight(0))
            .AddColumn(new TableColumn(string.Empty).RightAligned().NoWrap().PadLeft(0).PadRight(0));

        bannerTable.AddRow(
            new Markup($"[{EmphasisTag}]Azure Functions CLI[/]  [{MutedTag}]Host {Markup.Escape(version)}[/]"),
            new Markup($"[{HyperlinkTag}]{Markup.Escape(listen)}[/]"));

        Panel bannerPanel = new Panel(bannerTable)
            .Border(BoxBorder.Rounded)
            .BorderStyle(Theme.Muted)
            .Expand();

        IRenderable functions = snapshot.Functions.Count switch
        {
            0 => new Markup($"[{MutedTag}]  No functions loaded yet…[/]"),
            <= 8 => BuildFunctionsTable(snapshot.Functions, snapshot.ListenUri),
            _ => BuildFunctionsStatusStrip(snapshot),
        };

        string status = BuildStatusLine(snapshot);

        return new Rows(
            bannerPanel,
            new Padder(functions).PadTop(0).PadBottom(0),
            new Markup($"  [{MutedTag}]{Markup.Escape(status)}[/]"));
    }

    private IRenderable BuildFunctionsTable(IReadOnlyList<FunctionInfo> functions, string? listenUri)
    {
        Table table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn(string.Empty).PadLeft(2).PadRight(2).NoWrap())
            .AddColumn(new TableColumn(string.Empty).PadRight(2).NoWrap())
            .AddColumn(new TableColumn(string.Empty).PadRight(2).NoWrap())
            .AddColumn(new TableColumn(string.Empty).PadRight(2).NoWrap());

        table.AddRow(
            new Markup($"[{EmphasisTag}]Function[/]"),
            new Markup($"[{EmphasisTag}]Trigger[/]"),
            new Markup($"[{EmphasisTag}]Route / Source[/]"),
            new Markup($"[{EmphasisTag}]Status[/]"));

        foreach (FunctionInfo fn in functions.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            string color = _palette.GetColorFor(fn.Name);
            string routeMarkup = HttpRouteFormatter.FormatRouteMarkup(fn, listenUri);

            table.AddRow(
                new Markup($"[{color}]{Markup.Escape(fn.Name)}[/]"),
                new Markup(Markup.Escape(FormatTrigger(fn.TriggerType))),
                new Markup($"[{MutedTag}]{routeMarkup}[/]"),
                new Markup(FormatStatus(fn)));
        }

        return table;
    }

    private IRenderable BuildFunctionsStatusStrip(DashboardSnapshot snapshot)
    {
        int ready = snapshot.Functions.Count(f => f.Status == FunctionStatus.Ready);
        int active = snapshot.Functions.Count(f => f.Status == FunctionStatus.Active);
        int errored = snapshot.Functions.Count(f => f.Status == FunctionStatus.Error);

        return new Markup(string.Create(
            CultureInfo.InvariantCulture,
            $"  [{EmphasisTag}]{snapshot.Functions.Count}[/] functions  ·  [{SuccessTag}]● {ready} ready[/]  ·  [{ActiveTag}]◉ {active} active[/]  ·  [{ErrorTag}]✗ {errored} error{(errored == 1 ? string.Empty : "s")}[/]"));
    }

    private static string BuildStatusLine(DashboardSnapshot snapshot)
    {
        string state = snapshot.HostState switch
        {
            HostLifecycleState.Starting => "starting…",
            HostLifecycleState.Ready => "ready",
            HostLifecycleState.Recycling => "recycling…",
            HostLifecycleState.Stopped => "stopped",
            _ => "—",
        };

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Host {state}  ·  {snapshot.Functions.Count} functions  ·  {snapshot.TotalInvocations} invocations  ·  {snapshot.ErrorCount} error{(snapshot.ErrorCount == 1 ? string.Empty : "s")}  ·  Press Ctrl+C to stop");
    }

    private static string FormatTrigger(string trigger) => trigger switch
    {
        "http" => "HTTP",
        "queue" => "Queue",
        "timer" => "Timer",
        "blob" => "Blob",
        "eventhub" => "EventHub",
        "servicebus" => "ServiceBus",
        _ => trigger,
    };

    private string FormatStatus(FunctionInfo fn) => fn.Status switch
    {
        FunctionStatus.Active when fn.ActiveInvocations > 1 =>
            $"[{ActiveTag}]◉ Active ({fn.ActiveInvocations})[/]",
        FunctionStatus.Active => $"[{ActiveTag}]◉ Active[/]",
        FunctionStatus.Error => $"[{ErrorTag}]✗ Error[/]",
        _ => $"[{SuccessTag}]● Ready[/]",
    };

    private IRenderable FormatLogLine(HostLogEntry entry, IReadOnlyList<DashboardEvent> events)
    {
        string ts = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        // Synthetic events drive special formatting.
        foreach (DashboardEvent ev in events)
        {
            switch (ev)
            {
                case HostStateChangedEvent hs:
                    return new Markup(string.Create(CultureInfo.InvariantCulture,
                        $" [{MutedTag}]{ts}[/]  [{MutedTag}][[host]][/]             {Markup.Escape(DescribeHostState(hs))}"));

                case FunctionDiscoveredEvent fd:
                {
                    string color = _palette.GetColorFor(fd.Function.Name);
                    string routeMarkup = HttpRouteFormatter.FormatRouteMarkup(fd.Function, _state.Snapshot().ListenUri);
                    return new Markup(string.Create(CultureInfo.InvariantCulture,
                        $" [{MutedTag}]{ts}[/]  [{color}]{Markup.Escape(fd.Function.Name),-18}[/]  loaded  [{MutedTag}]{Markup.Escape(fd.Function.TriggerType)} {routeMarkup}[/]"));
                }

                case InvocationStartedEvent inv:
                {
                    string color = _palette.GetColorFor(inv.Function);
                    string detail = string.Empty;
                    if (inv.Attributes.TryGetValue(HostLogAttributeKeys.HttpMethod, out object? method) &&
                        inv.Attributes.TryGetValue(HostLogAttributeKeys.HttpTarget, out object? target))
                    {
                        detail = $"{method} {target}";
                    }

                    return new Markup(string.Create(CultureInfo.InvariantCulture,
                        $" [{MutedTag}]{ts}[/]  [{color}]{Markup.Escape(inv.Function),-18}[/]  [{SuccessTag}]→[/]  {Markup.Escape(detail)}"));
                }

                case InvocationCompletedEvent inv:
                {
                    string color = _palette.GetColorFor(inv.Function);
                    bool failed = string.Equals(inv.Result, "failed", StringComparison.OrdinalIgnoreCase);
                    string arrow = failed ? $"[{ErrorTag}]✗[/]" : $"[{SuccessTag}]←[/]";
                    string suffix = failed
                        ? $"[{ErrorTag}]{Markup.Escape(inv.ErrorType ?? string.Empty)}: {Markup.Escape(inv.ErrorMessage ?? string.Empty)}[/]"
                        : $"[{MutedTag}]{(inv.DurationMs.HasValue ? ((long)inv.DurationMs.Value).ToString(CultureInfo.InvariantCulture) + "ms" : string.Empty)}[/]";

                    return new Markup(string.Create(CultureInfo.InvariantCulture,
                        $" [{MutedTag}]{ts}[/]  [{color}]{Markup.Escape(inv.Function),-18}[/]  {arrow}  {suffix}"));
                }
            }
        }

        // Plain log line — color by level; function name (if any) gets palette color.
        string? functionName = entry.GetAttribute<string>(HostLogAttributeKeys.FunctionName);
        string nameMarkup = functionName is not null
            ? $"[{_palette.GetColorFor(functionName)}]{Markup.Escape(functionName),-18}[/]"
            : $"[{MutedTag}]{Markup.Escape(entry.Category),-18}[/]";

        string levelMarkup = entry.Level switch
        {
            LogLevel.Error or LogLevel.Critical => $"[{ErrorTag}]✗[/]",
            LogLevel.Warning => $"[{WarningTag}]![/]",
            _ => $"[{MutedTag}]·[/]",
        };

        return new Markup(string.Create(CultureInfo.InvariantCulture,
            $" [{MutedTag}]{ts}[/]  {nameMarkup}  {levelMarkup}  {Markup.Escape(entry.Message)}"));
    }

    private static string DescribeHostState(HostStateChangedEvent hs) => (hs.From, hs.To) switch
    {
        (_, HostLifecycleState.Ready) when hs.DurationMs is { } d => $"Host ready ({(d / 1000.0).ToString("F1", CultureInfo.InvariantCulture)}s)",
        (_, HostLifecycleState.Ready) => "Host ready",
        (_, HostLifecycleState.Recycling) when !string.IsNullOrEmpty(hs.Trigger) => $"Recycling (file changed: {hs.Trigger})",
        (_, HostLifecycleState.Recycling) => "Recycling",
        (_, HostLifecycleState.Starting) => "Host starting",
        (_, HostLifecycleState.Stopped) => "Host stopped",
        _ => $"Host {hs.To.ToString().ToLowerInvariant()}",
    };
}
