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
/// Prototype scope: the renderer demonstrates the live header / live log tail UX against the scripted event source.
/// </remarks>
internal sealed class CompactRenderer(
    IInteractionService interaction,
    FunctionPalette palette,
    IAnsiConsole? console = null,
    DashboardRunInfo? runInfo = null) : IDashboardRenderer, IDashboardShutdownRequester
{
    private const int MaxLogTailLines = 200;
    private const int HelpOverlayCommandRows = 15;
    private const int HelpOverlayLines = HelpOverlayCommandRows + 3;
    private const int SearchOverlayChromeLines = 5;

    private static readonly IComparer<string> _functionNameComparer = new FunctionNameComparer();

    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly FunctionPalette _palette = palette ?? throw new ArgumentNullException(nameof(palette));
    private readonly IAnsiConsole _console = console ?? AnsiConsole.Console;
    private readonly DashboardRunInfo _runInfo = runInfo ?? new();
    private readonly Lock _stateLock = new();
    private readonly Lock _uiLock = new();
    private readonly Queue<LogLine> _logTail = new();

    private DashboardState _state = null!;
    private CancellationTokenSource? _liveCts;
    private SemaphoreSlim? _redrawSignal;
    private Task? _liveTask;
    private Task? _inputTask;
    private int _lastKnownWidth;
    private int _lastKnownHeight;
    private bool _helpOpen;
    private bool _functionBrowserOpen;
    private bool _functionSearchOpen;
    private int _functionBrowserSelectedIndex;
    private int _functionBrowserRowOffset;
    private int _functionSearchSelectedIndex;
    private int _functionSearchRowOffset;
    private string _functionSearchQuery = string.Empty;
    private string? _activeFunctionFilter;
    private int _logScrollOffset;
    private bool _errorsOnly;
    private LogLevel _minimumLogLevel = LogLevel.Information;

    private ITheme Theme => _interaction.Theme;

    private string HeadingTag => field ??= Theme.Heading.ToMarkup();
    private string CommandTag => field ??= Theme.Command.ToMarkup();
    private string MutedTag => field ??= Theme.Muted.ToMarkup();
    private string EmphasisTag => field ??= Theme.Emphasis.ToMarkup();
    private string SuccessTag => field ??= Theme.Success.ToMarkup();
    private string ErrorTag => field ??= Theme.Error.ToMarkup();
    private string WarningTag => field ??= Theme.Warning.ToMarkup();
    private string ActiveTag => field ??= Theme.Active.ToMarkup();
    private string HyperlinkTag => field ??= Theme.Hyperlink.ToMarkup();

    public event Action? ShutdownRequested;

    public Task OnStartAsync(DashboardState state, CancellationToken cancellationToken)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _redrawSignal = new SemaphoreSlim(initialCount: 1, maxCount: int.MaxValue);
        _liveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _console.Cursor.Hide();
        _liveTask = Task.Run(() => RunLiveLoopAsync(_liveCts.Token));

        if (_interaction.IsInteractive)
        {
            _inputTask = Task.Run(() => RunInputLoopAsync(_liveCts.Token), cancellationToken);
        }

        return Task.CompletedTask;
    }

    public Task OnEventAsync(HostLogEntry entry, IReadOnlyList<DashboardEvent> events, CancellationToken cancellationToken)
    {
        if (ShouldSuppress(entry, events))
        {
            return Task.CompletedTask;
        }

        bool isError = IsErrorLogLine(entry, events);
        var line = new LogLine(
            FormatLogLine(entry, events),
            GetFunctionName(entry, events),
            isError,
            GetEffectiveLogLevel(entry, isError));
        lock (_stateLock)
        {
            _logTail.Enqueue(line);
            while (_logTail.Count > MaxLogTailLines)
            {
                _logTail.Dequeue();
            }
        }

        lock (_uiLock)
        {
            if (_logScrollOffset > 0 && MatchesLogFilters(line, _activeFunctionFilter, _errorsOnly, _minimumLogLevel))
            {
                _logScrollOffset = Math.Min(MaxLogTailLines, _logScrollOffset + 1);
            }
        }

        SignalRedraw();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Filters out raw log lines that are already covered by a synthetic
    /// event in this batch, plus the classic Functions <c>Executing '...'</c> /
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

        if (_inputTask is { } inputTask)
        {
            try
            {
                await inputTask;
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

    private async Task RunInputLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ConsoleKeyInfo? key = await _console.Input.ReadKeyAsync(intercept: true, cancellationToken);
            if (key is null)
            {
                continue;
            }

            if (HandleKey(key.Value))
            {
                SignalRedraw();
            }
        }
    }

    private bool HandleKey(ConsoleKeyInfo key)
    {
        DashboardSnapshot snapshot = _state.Snapshot();
        FunctionInfo[] functions = GetSortedFunctions(snapshot);

        lock (_uiLock)
        {
            if (_functionSearchOpen)
            {
                return HandleFunctionSearchKey(key, functions);
            }

            if (key.KeyChar == '?')
            {
                ToggleHelpOverlay();
                return true;
            }

            if (key.KeyChar == '/')
            {
                OpenFunctionSearch();
                return true;
            }

            switch (key.Key)
            {
                case ConsoleKey.T:
                    ToggleFunctionBrowser(functions);
                    return true;

                case ConsoleKey.C:
                    ClearVisibleLogs();
                    return true;

                case ConsoleKey.E:
                    _errorsOnly = !_errorsOnly;
                    ResetLogScroll();
                    return true;

                case ConsoleKey.F:
                    CycleFunctionFilter(functions);
                    return functions.Length > 0 || _activeFunctionFilter is not null;

                case ConsoleKey.D1:
                case ConsoleKey.NumPad1:
                    return SetMinimumLogLevel(LogLevel.Information);

                case ConsoleKey.D2:
                case ConsoleKey.NumPad2:
                    return SetMinimumLogLevel(LogLevel.Warning);

                case ConsoleKey.D3:
                case ConsoleKey.NumPad3:
                    return SetMinimumLogLevel(LogLevel.Error);

                case ConsoleKey.Q:
                    ShutdownRequested?.Invoke();
                    return true;

                case ConsoleKey.Escape when _helpOpen || _functionBrowserOpen:
                    _helpOpen = false;
                    _functionBrowserOpen = false;
                    return true;

                case ConsoleKey.A when _activeFunctionFilter is not null:
                    _activeFunctionFilter = null;
                    ResetLogScroll();
                    return true;

                case ConsoleKey.Enter when _functionBrowserOpen && functions.Length > 0:
                    _functionBrowserSelectedIndex = Math.Clamp(_functionBrowserSelectedIndex, 0, functions.Length - 1);
                    _activeFunctionFilter = functions[_functionBrowserSelectedIndex].Name;
                    ResetLogScroll();
                    _functionBrowserOpen = false;
                    return true;

                case ConsoleKey.UpArrow when _functionBrowserOpen:
                    MoveFunctionBrowserSelection(functions, -1);
                    return true;

                case ConsoleKey.DownArrow when _functionBrowserOpen:
                    MoveFunctionBrowserSelection(functions, 1);
                    return true;

                case ConsoleKey.PageUp when _functionBrowserOpen:
                    MoveFunctionBrowserSelection(functions, -Math.Max(1, GetFunctionBrowserVisibleRows(functions.Length)));
                    return true;

                case ConsoleKey.PageDown when _functionBrowserOpen:
                    MoveFunctionBrowserSelection(functions, Math.Max(1, GetFunctionBrowserVisibleRows(functions.Length)));
                    return true;

                case ConsoleKey.PageUp when !_helpOpen:
                    ScrollLogs(GetLogScrollStep(snapshot));
                    return true;

                case ConsoleKey.PageDown when !_helpOpen:
                    return ScrollLogs(-GetLogScrollStep(snapshot));

                case ConsoleKey.Home when _functionBrowserOpen:
                    _functionBrowserSelectedIndex = 0;
                    return functions.Length > 0;

                case ConsoleKey.End when _functionBrowserOpen:
                    _functionBrowserSelectedIndex = Math.Max(0, functions.Length - 1);
                    return functions.Length > 0;

                case ConsoleKey.Home when !_helpOpen:
                    ScrollLogs(MaxLogTailLines);
                    return true;

                case ConsoleKey.End when !_helpOpen:
                    return ResetLogScroll();

                case ConsoleKey.LeftArrow when _functionBrowserOpen:
                    MoveFunctionBrowserSelection(functions, -GetFunctionBrowserTotalRows(functions.Length));
                    return true;

                case ConsoleKey.RightArrow when _functionBrowserOpen:
                    MoveFunctionBrowserSelection(functions, GetFunctionBrowserTotalRows(functions.Length));
                    return true;
            }
        }

        return false;
    }

    private void ToggleFunctionBrowser(FunctionInfo[] functions)
    {
        _functionBrowserOpen = !_functionBrowserOpen;
        if (!_functionBrowserOpen)
        {
            return;
        }

        _helpOpen = false;
        _functionSearchOpen = false;
        _functionBrowserRowOffset = 0;
        if (_activeFunctionFilter is null)
        {
            _functionBrowserSelectedIndex = 0;
            return;
        }

        int index = Array.FindIndex(functions, f => string.Equals(f.Name, _activeFunctionFilter, StringComparison.Ordinal));
        _functionBrowserSelectedIndex = Math.Max(0, index);
    }

    private void MoveFunctionBrowserSelection(FunctionInfo[] functions, int delta)
    {
        if (functions.Length == 0)
        {
            _functionBrowserSelectedIndex = 0;
            _functionBrowserRowOffset = 0;
            return;
        }

        _functionBrowserSelectedIndex = Math.Clamp(_functionBrowserSelectedIndex + delta, 0, functions.Length - 1);
    }

    private void ToggleHelpOverlay()
    {
        _helpOpen = !_helpOpen;
        if (_helpOpen)
        {
            _functionBrowserOpen = false;
            _functionSearchOpen = false;
        }
    }

    private void OpenFunctionSearch()
    {
        _functionSearchOpen = true;
        _helpOpen = false;
        _functionBrowserOpen = false;
        _functionSearchQuery = string.Empty;
        _functionSearchSelectedIndex = 0;
        _functionSearchRowOffset = 0;
    }

    private bool HandleFunctionSearchKey(ConsoleKeyInfo key, FunctionInfo[] functions)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                _functionSearchOpen = false;
                return true;

            case ConsoleKey.Enter:
            {
                FunctionInfo[] matches = GetFunctionSearchMatches(functions, _functionSearchQuery);
                if (matches.Length == 0)
                {
                    return false;
                }

                _functionSearchSelectedIndex = Math.Clamp(_functionSearchSelectedIndex, 0, matches.Length - 1);
                _activeFunctionFilter = matches[_functionSearchSelectedIndex].Name;
                ResetLogScroll();
                _functionSearchOpen = false;
                return true;
            }

            case ConsoleKey.UpArrow:
                MoveFunctionSearchSelection(functions, -1);
                return true;

            case ConsoleKey.DownArrow:
                MoveFunctionSearchSelection(functions, 1);
                return true;

            case ConsoleKey.Backspace when _functionSearchQuery.Length > 0:
                _functionSearchQuery = _functionSearchQuery[..^1];
                _functionSearchSelectedIndex = 0;
                _functionSearchRowOffset = 0;
                return true;
        }

        if (!char.IsControl(key.KeyChar))
        {
            _functionSearchQuery += key.KeyChar;
            _functionSearchSelectedIndex = 0;
            _functionSearchRowOffset = 0;
            return true;
        }

        return false;
    }

    private void MoveFunctionSearchSelection(FunctionInfo[] functions, int delta)
    {
        FunctionInfo[] matches = GetFunctionSearchMatches(functions, _functionSearchQuery);
        if (matches.Length == 0)
        {
            _functionSearchSelectedIndex = 0;
            _functionSearchRowOffset = 0;
            return;
        }

        _functionSearchSelectedIndex = Math.Clamp(_functionSearchSelectedIndex + delta, 0, matches.Length - 1);
    }

    private void ClearVisibleLogs()
    {
        lock (_stateLock)
        {
            _logTail.Clear();
        }

        ResetLogScroll();
    }

    private void CycleFunctionFilter(FunctionInfo[] functions)
    {
        if (functions.Length == 0)
        {
            _activeFunctionFilter = null;
            ResetLogScroll();
            return;
        }

        if (_activeFunctionFilter is null)
        {
            _activeFunctionFilter = functions[0].Name;
            ResetLogScroll();
            return;
        }

        int index = Array.FindIndex(functions, f => string.Equals(f.Name, _activeFunctionFilter, StringComparison.Ordinal));
        _activeFunctionFilter = index >= 0 && index < functions.Length - 1
            ? functions[index + 1].Name
            : null;
        ResetLogScroll();
    }

    private bool SetMinimumLogLevel(LogLevel minimumLogLevel)
    {
        if (_minimumLogLevel == minimumLogLevel)
        {
            return false;
        }

        _minimumLogLevel = minimumLogLevel;
        ResetLogScroll();
        return true;
    }

    private int GetLogScrollStep(DashboardSnapshot snapshot)
    {
        int logBudget = ComputeLogBudgetCore(snapshot, _helpOpen, functionSearchOpen: false, functionBrowserOpen: false, searchQuery: string.Empty);
        return Math.Max(1, logBudget - 1);
    }

    private bool ScrollLogs(int delta)
    {
        int previous = _logScrollOffset;
        _logScrollOffset = Math.Clamp(_logScrollOffset + delta, 0, MaxLogTailLines);
        return _logScrollOffset != previous;
    }

    private bool ResetLogScroll()
    {
        if (_logScrollOffset == 0)
        {
            return false;
        }

        _logScrollOffset = 0;
        return true;
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

        LogLine[] tail;
        lock (_stateLock)
        {
            tail = [.. _logTail];
        }

        string? activeFunctionFilter;
        bool errorsOnly;
        LogLevel minimumLogLevel;
        lock (_uiLock)
        {
            activeFunctionFilter = _activeFunctionFilter;
            errorsOnly = _errorsOnly;
            minimumLogLevel = _minimumLogLevel;
        }

        if (activeFunctionFilter is not null)
        {
            tail = [.. tail.Where(line =>
                line.FunctionName is null
                || string.Equals(line.FunctionName, activeFunctionFilter, StringComparison.Ordinal))];
        }

        if (errorsOnly)
        {
            tail = [.. tail.Where(line => line.IsError)];
        }

        tail = [.. tail.Where(line => line.Level >= minimumLogLevel)];

        int logBudget = ComputeLogBudget(snapshot);
        int logScrollOffset;
        lock (_uiLock)
        {
            _logScrollOffset = Math.Clamp(_logScrollOffset, 0, Math.Max(0, tail.Length - logBudget));
            logScrollOffset = _logScrollOffset;
        }

        if (tail.Length > logBudget)
        {
            int start = Math.Max(0, tail.Length - logBudget - logScrollOffset);
            tail = tail[start..Math.Min(tail.Length, start + logBudget)];
        }

        IRenderable logRows = BuildLogRows(tail, activeFunctionFilter, logBudget);
        IRenderable footer = BuildFooterCore(snapshot, activeFunctionFilter, errorsOnly, minimumLogLevel);

        var rows = new Rows(
            header,
            new Rule("logs").LeftJustified().RuleStyle(Theme.Muted),
            logRows,
            footer);

        return rows;
    }

    private IRenderable BuildLogRows(LogLine[] tail, string? activeFunctionFilter, int logBudget)
    {
        if (logBudget <= 0)
        {
            return new Rows(Array.Empty<IRenderable>());
        }

        List<IRenderable> rows = tail.Length == 0
            ? [new Markup($"[{MutedTag}]{Markup.Escape(GetEmptyLogMessage(activeFunctionFilter))}[/]")]
            : [.. tail.Select(static line => line.Renderable)];

        while (rows.Count < logBudget)
        {
            rows.Add(new Markup(string.Empty));
        }

        return new Rows(rows);
    }

    private int ComputeLogBudget(DashboardSnapshot snapshot)
    {
        bool helpOpen;
        bool browserOpen;
        bool searchOpen;
        string searchQuery;
        lock (_uiLock)
        {
            helpOpen = _helpOpen;
            browserOpen = _functionBrowserOpen;
            searchOpen = _functionSearchOpen;
            searchQuery = _functionSearchQuery;
        }

        return ComputeLogBudgetCore(snapshot, helpOpen, searchOpen, browserOpen, searchQuery);
    }

    private int ComputeLogBudgetCore(
        DashboardSnapshot snapshot,
        bool helpOpen,
        bool functionSearchOpen,
        bool functionBrowserOpen,
        string searchQuery)
    {
        // Spectre reports sentinel values when stdout isn't a real terminal
        // (e.g., redirected to a pipe). Clamp to a sensible default so the
        // compact renderer still produces useful output if it's somehow
        // reached without the terminal-safety fallback firing.
        int viewportHeight = GetViewportHeight();

        // Chrome lines breakdown (matches BuildLayout output):
        //   bannerPanel (rounded box):              3 lines
        //   functions block (count-dependent):      see switch
        //   "logs" rule:                            1 line
        //   footer line:                            1 line
        //   safety pad (resize/wrap slack):         2 lines
        int functionsLines = ComputeHeaderLineCount(snapshot, helpOpen, functionSearchOpen, functionBrowserOpen, searchQuery);
        if (helpOpen || functionBrowserOpen || functionSearchOpen)
        {
            // Overlay panel + "logs" rule + footer. Do not reserve extra
            // slack here: if the target exceeds the viewport, Spectre crops
            // from the top and hides the overlay header.
            int reservedForOverlay = functionsLines + 1 + 1;
            return Math.Max(0, viewportHeight - reservedForOverlay);
        }

        // Always reserve at least 3 lines for logs so the dashboard remains
        // useful even on very small terminals (the header simply overflows).
        int reservedForHeader = 3 + functionsLines + 1 + 1 + 2;
        return Math.Max(3, viewportHeight - reservedForHeader);
    }

    private int ComputeHeaderLineCount(
        DashboardSnapshot snapshot,
        bool helpOpen,
        bool functionSearchOpen,
        bool functionBrowserOpen,
        string searchQuery)
        => helpOpen
            ? HelpOverlayLines
            : functionSearchOpen
            ? GetFunctionSearchVisibleRows(GetFunctionSearchMatches(GetSortedFunctions(snapshot), searchQuery).Length) + SearchOverlayChromeLines
            : functionBrowserOpen
            // Panel border (2) + visible grid rows + spacer/footer (2).
            ? GetFunctionBrowserVisibleRows(snapshot.Functions.Count) + 4
            : snapshot.Functions.Count switch
            {
                0 => 1,
                <= 8 => snapshot.Functions.Count + 1, // 1 column-header row + N data rows
                _ => GetFunctionsBlockLineCount(snapshot.Functions.Count),
            };

    private IRenderable BuildHeader(DashboardSnapshot snapshot)
    {
        bool helpOpen;
        bool functionSearchOpen;
        bool functionBrowserOpen;
        lock (_uiLock)
        {
            helpOpen = _helpOpen;
            functionSearchOpen = _functionSearchOpen;
            functionBrowserOpen = _functionBrowserOpen;
        }

        if (helpOpen)
        {
            return BuildHelpOverlay();
        }

        if (functionSearchOpen)
        {
            return BuildFunctionSearch(snapshot);
        }

        if (functionBrowserOpen)
        {
            return BuildFunctionBrowser(snapshot);
        }

        string version = snapshot.HostVersion ?? "—";
        string listen = snapshot.ListenUri ?? "—";
        string profile = string.IsNullOrWhiteSpace(_runInfo.ProfileName) ? "none" : _runInfo.ProfileName;
        string stack = string.IsNullOrWhiteSpace(_runInfo.StackName) ? "unknown" : _runInfo.StackName;

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
            new Markup($"[{WarningTag}]:high_voltage:[/] [{EmphasisTag}]Azure Functions CLI[/]  [{MutedTag}]Host:[/] [{EmphasisTag}]{Markup.Escape(version)}[/][{MutedTag}] · Profile:[/] [{EmphasisTag}]{Markup.Escape(profile)}[/][{MutedTag}] · Stack:[/] [{EmphasisTag}]{Markup.Escape(stack)}[/]"),
            new Markup($"[{HyperlinkTag}]{Markup.Escape(listen)}[/]"));

        Panel bannerPanel = new Panel(bannerTable)
            .Border(BoxBorder.Rounded)
            .BorderStyle(Theme.Muted)
            .Expand();

        int truncatedRows = GetTruncatedFunctionVisibleRows(snapshot.Functions.Count);
        IRenderable functions = snapshot.Functions.Count switch
        {
            0 => new Markup($"[{MutedTag}]  No functions loaded yet…[/]"),
            <= 8 => BuildFunctionsTable(GetSortedFunctions(snapshot), snapshot.ListenUri),
            _ when truncatedRows > 0 => BuildTruncatedFunctionsTable(snapshot, truncatedRows),
            _ => BuildFunctionsStatusStrip(snapshot),
        };

        return new Rows(
            bannerPanel,
            new Padder(functions).PadTop(0).PadBottom(0));
    }

    private Table BuildFunctionsTable(IReadOnlyList<FunctionInfo> functions, string? listenUri)
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

        foreach (FunctionInfo fn in functions)
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

    private IRenderable BuildTruncatedFunctionsTable(DashboardSnapshot snapshot, int visibleRows)
    {
        FunctionInfo[] ordered = [.. snapshot.Functions
            .OrderBy(GetFunctionHeaderPriority)
            .ThenByDescending(f => f.LastInvocationAt ?? DateTimeOffset.MinValue)
            .ThenBy(f => f.Name, _functionNameComparer)];

        Table table = BuildFunctionsTable(ordered.Take(visibleRows).ToArray(), snapshot.ListenUri);
        int hidden = ordered.Length - visibleRows;
        if (hidden > 0)
        {
            table.AddRow(
                new Markup($"[{MutedTag}]  +{hidden.ToString(CultureInfo.InvariantCulture)} more[/]"),
                new Markup(string.Empty),
                new Markup($"[{MutedTag}]press t to view all[/]"),
                new Markup(string.Empty));
        }

        return table;
    }

    private IRenderable BuildHelpOverlay()
    {
        Table table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .Expand()
            .AddColumn(new TableColumn(string.Empty).PadLeft(1).PadRight(3).NoWrap())
            .AddColumn(new TableColumn(string.Empty).PadRight(0));

        AddHelpRow(table, "?", "Toggle this help panel.");
        AddHelpRow(table, "t", "Open the function browser.");
        AddHelpRow(table, "/", "Search functions by name, trigger, or route.");
        AddHelpRow(table, "↑/↓", "Move selection in the function browser.");
        AddHelpRow(table, "PgUp/PgDn", "Scroll logs; in the function browser, jump through functions.");
        AddHelpRow(table, "Home/End", "Jump to oldest logs / latest logs, or first / last function in the browser.");
        AddHelpRow(table, "Enter", "Filter logs to the selected function in the function browser.");
        AddHelpRow(table, "a", "Clear the active function filter.");
        AddHelpRow(table, "f", "Cycle the active function filter.");
        AddHelpRow(table, "c", "Clear visible log output.");
        AddHelpRow(table, "e", "Toggle errors-only log view.");
        AddHelpRow(table, "1/2/3", "Set visible log level: info, warning, or error.");
        AddHelpRow(table, "q", "Stop the host.");
        AddHelpRow(table, "Esc", "Close the active overlay.");
        AddHelpRow(table, "Ctrl+C", "Stop the host.");

        var panel = new Panel(new Rows(
            new Markup($"[{MutedTag}]Available compact-mode controls[/]"),
            table))
        {
            Header = new PanelHeader("Help"),
            Border = BoxBorder.Rounded,
            BorderStyle = Theme.Muted,
            Expand = true,
        };

        return panel;
    }

    private IRenderable BuildFunctionSearch(DashboardSnapshot snapshot)
    {
        FunctionInfo[] functions = GetSortedFunctions(snapshot);
        string query;
        int selectedIndex;
        int rowOffset;
        FunctionInfo[] matches;
        int visibleRows;
        lock (_uiLock)
        {
            query = _functionSearchQuery;
            matches = GetFunctionSearchMatches(functions, query);
            visibleRows = GetFunctionSearchVisibleRows(matches.Length);
            if (matches.Length == 0)
            {
                _functionSearchSelectedIndex = 0;
                _functionSearchRowOffset = 0;
            }
            else
            {
                _functionSearchSelectedIndex = Math.Clamp(_functionSearchSelectedIndex, 0, matches.Length - 1);
                int maxOffset = Math.Max(0, matches.Length - visibleRows);
                if (_functionSearchRowOffset > _functionSearchSelectedIndex)
                {
                    _functionSearchRowOffset = _functionSearchSelectedIndex;
                }
                else if (_functionSearchRowOffset + visibleRows <= _functionSearchSelectedIndex)
                {
                    _functionSearchRowOffset = _functionSearchSelectedIndex - visibleRows + 1;
                }

                _functionSearchRowOffset = Math.Clamp(_functionSearchRowOffset, 0, maxOffset);
            }

            selectedIndex = _functionSearchSelectedIndex;
            rowOffset = _functionSearchRowOffset;
        }

        IRenderable results = matches.Length == 0
            ? new Markup($"[{MutedTag}]  No functions match \"{Markup.Escape(query)}\"[/]")
            : BuildFunctionSearchResults(matches, visibleRows, rowOffset, selectedIndex);

        string displayQuery = query.Length == 0 ? "type to search" : query;
        var panel = new Panel(new Rows(
            new Markup($"[{MutedTag}]Search:[/] [{EmphasisTag}]{Markup.Escape(displayQuery)}[/]"),
            results,
            new Markup(string.Empty),
            new Markup($"[{MutedTag}]Type to filter · Up/Down select · Enter apply · Esc cancel[/]")))
        {
            Header = new PanelHeader("Search functions"),
            Border = BoxBorder.Rounded,
            BorderStyle = Theme.Muted,
            Expand = true,
        };

        return panel;
    }

    private IRenderable BuildFunctionSearchResults(FunctionInfo[] matches, int visibleRows, int rowOffset, int selectedIndex)
    {
        Table table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .Expand()
            .AddColumn(new TableColumn(string.Empty).PadLeft(1).PadRight(2).NoWrap())
            .AddColumn(new TableColumn(string.Empty).PadRight(2).NoWrap())
            .AddColumn(new TableColumn(string.Empty).PadRight(0));

        for (int i = 0; i < visibleRows; i++)
        {
            int index = rowOffset + i;
            if ((uint)index >= (uint)matches.Length)
            {
                break;
            }

            FunctionInfo fn = matches[index];
            string marker = index == selectedIndex
                ? $"[{EmphasisTag}]>[/]"
                : " ";
            string color = _palette.GetColorFor(fn.Name);
            string route = string.IsNullOrEmpty(fn.Route) ? FormatTrigger(fn.TriggerType) : fn.Route;

            table.AddRow(
                new Markup(marker),
                new Markup($"[{color}]{Markup.Escape(fn.Name)}[/]"),
                new Markup($"[{MutedTag}]{Markup.Escape(route)}[/]"));
        }

        return table;
    }

    private void AddHelpRow(Table table, string key, string description)
    {
        table.AddRow(
            new Markup($"[{CommandTag}]{Markup.Escape(key)}[/]"),
            new Markup($"[{MutedTag}]{Markup.Escape(description)}[/]"));
    }

    private IRenderable BuildFunctionBrowser(DashboardSnapshot snapshot)
    {
        FunctionInfo[] functions = GetSortedFunctions(snapshot);
        int totalRows = GetFunctionBrowserTotalRows(functions.Length);
        int visibleRows = GetFunctionBrowserVisibleRows(functions.Length);
        int selectedIndex;
        int rowOffset;

        lock (_uiLock)
        {
            if (functions.Length == 0)
            {
                _functionBrowserSelectedIndex = 0;
                _functionBrowserRowOffset = 0;
            }
            else
            {
                _functionBrowserSelectedIndex = Math.Clamp(_functionBrowserSelectedIndex, 0, functions.Length - 1);
                int selectedRow = GetFunctionBrowserRow(_functionBrowserSelectedIndex, totalRows);
                int maxOffset = Math.Max(0, totalRows - visibleRows);

                if (_functionBrowserRowOffset > selectedRow)
                {
                    _functionBrowserRowOffset = selectedRow;
                }
                else if (_functionBrowserRowOffset + visibleRows <= selectedRow)
                {
                    _functionBrowserRowOffset = selectedRow - visibleRows + 1;
                }

                _functionBrowserRowOffset = Math.Clamp(_functionBrowserRowOffset, 0, maxOffset);
            }

            selectedIndex = _functionBrowserSelectedIndex;
            rowOffset = _functionBrowserRowOffset;
        }

        IRenderable content = functions.Length == 0
            ? new Markup($"[{MutedTag}]No functions loaded yet…[/]")
            : BuildFunctionBrowserGrid(functions, totalRows, visibleRows, rowOffset, selectedIndex);

        var footer = new Markup($"[{MutedTag}]Up/Down navigate · Enter filter · / search · f next · a all · t/Esc close · q/Ctrl+C[/]");
        var panel = new Panel(new Rows(content, new Markup(string.Empty), footer))
        {
            Header = new PanelHeader(string.Create(CultureInfo.InvariantCulture, $"Functions ({functions.Length})")),
            Border = BoxBorder.Rounded,
            BorderStyle = Theme.Muted,
            Expand = true,
        };

        return panel;
    }

    private IRenderable BuildFunctionBrowserGrid(
        FunctionInfo[] functions,
        int totalRows,
        int visibleRows,
        int rowOffset,
        int selectedIndex)
    {
        Table table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .Expand()
            .AddColumn(new TableColumn(string.Empty).PadLeft(1).PadRight(4).NoWrap())
            .AddColumn(new TableColumn(string.Empty).PadLeft(1).PadRight(0).NoWrap());

        for (int row = 0; row < visibleRows; row++)
        {
            int leftIndex = rowOffset + row;
            int rightIndex = leftIndex + totalRows;

            table.AddRow(
                new Markup(FormatFunctionBrowserCell(functions, leftIndex, selectedIndex)),
                new Markup(FormatFunctionBrowserCell(functions, rightIndex, selectedIndex)));
        }

        return table;
    }

    private string FormatFunctionBrowserCell(FunctionInfo[] functions, int index, int selectedIndex)
    {
        if ((uint)index >= (uint)functions.Length)
        {
            return string.Empty;
        }

        FunctionInfo fn = functions[index];
        string marker = index == selectedIndex
            ? $"[{EmphasisTag}]>[/]"
            : " ";
        string status = FormatFunctionBrowserStatus(fn);
        string color = _palette.GetColorFor(fn.Name);
        string activeCount = fn.Status == FunctionStatus.Active && fn.ActiveInvocations > 1
            ? string.Create(CultureInfo.InvariantCulture, $" ({fn.ActiveInvocations})")
            : string.Empty;

        return $"{marker} {status} [{color}]{Markup.Escape(fn.Name)}[/]{activeCount}";
    }

    private string FormatFunctionBrowserStatus(FunctionInfo fn) => fn.Status switch
    {
        FunctionStatus.Active => $"[{ActiveTag}]◉[/]",
        FunctionStatus.Error => $"[{ErrorTag}]✗[/]",
        _ => $"[{SuccessTag}]●[/]",
    };

    private IRenderable BuildFunctionsStatusStrip(DashboardSnapshot snapshot)
    {
        int ready = snapshot.Functions.Count(f => f.Status == FunctionStatus.Ready);
        int active = snapshot.Functions.Count(f => f.Status == FunctionStatus.Active);
        int errored = snapshot.Functions.Count(f => f.Status == FunctionStatus.Error);

        return new Markup(string.Create(
            CultureInfo.InvariantCulture,
            $"  [{EmphasisTag}]{snapshot.Functions.Count}[/] functions  ·  [{SuccessTag}]● {ready} ready[/]  ·  [{ActiveTag}]◉ {active} active[/]  ·  [{ErrorTag}]✗ {errored} error{(errored == 1 ? string.Empty : "s")}[/]"));
    }

    private IRenderable BuildFooter(DashboardSnapshot snapshot, string? activeFunctionFilter)
        => BuildFooterCore(snapshot, activeFunctionFilter, errorsOnly: false, LogLevel.Information);

    private IRenderable BuildFooterCore(DashboardSnapshot snapshot, string? activeFunctionFilter, bool errorsOnly, LogLevel minimumLogLevel)
    {
        string state = snapshot.HostState switch
        {
            HostLifecycleState.Starting => "Starting…",
            HostLifecycleState.Ready => "Ready",
            HostLifecycleState.Recycling => "Recycling…",
            HostLifecycleState.Stopped => "Stopped",
            _ => "—",
        };

        string filter = activeFunctionFilter is not null
            ? $" · Filter {activeFunctionFilter}"
            : string.Empty;
        string errors = errorsOnly
            ? " · Errors only"
            : string.Empty;
        string level = $" · L:{FormatMinimumLogLevel(minimumLogLevel)}";
        int logScrollOffset;
        bool helpOpen;
        bool functionSearchOpen;
        bool functionBrowserOpen;
        lock (_uiLock)
        {
            logScrollOffset = _logScrollOffset;
            helpOpen = _helpOpen;
            functionSearchOpen = _functionSearchOpen;
            functionBrowserOpen = _functionBrowserOpen;
        }

        string logScroll = logScrollOffset > 0
            ? $" · Scrollback {logScrollOffset}"
            : string.Empty;
        string controls = (helpOpen, functionSearchOpen, functionBrowserOpen, activeFunctionFilter is not null) switch
        {
            (true, _, _, _) => "? close · Esc close · q/Ctrl+C",
            (_, true, _, _) => "type query · ↑/↓ select · Enter filter · Esc close",
            (_, _, true, _) => "↑/↓ navigate · Enter filter · / search · f next · t close · Esc close",
            (_, _, _, true) => "PgUp/PgDn logs · / search · f next · a all · c/e logs · ? · q/Ctrl+C",
            _ => "PgUp/PgDn logs · t funcs · / search · f filter · c/e logs · ? · q/Ctrl+C",
        };

        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"{state} · {snapshot.Functions.Count} functions · {snapshot.TotalInvocations} invocations · {snapshot.ErrorCount} error{(snapshot.ErrorCount == 1 ? string.Empty : "s")}{filter}{errors}{level}{logScroll} │ {controls}");

        return new Markup($"[{MutedTag}]{Markup.Escape(line)}[/]");
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

    private static FunctionInfo[] GetSortedFunctions(DashboardSnapshot snapshot)
        => [.. snapshot.Functions.OrderBy(f => f.Name, _functionNameComparer)];

    private int GetFunctionsBlockLineCount(int functionCount)
    {
        int visibleRows = GetTruncatedFunctionVisibleRows(functionCount);
        return visibleRows > 0
            ? visibleRows + 1 + (functionCount > visibleRows ? 1 : 0)
            : 1;
    }

    private int GetTruncatedFunctionVisibleRows(int functionCount)
    {
        if (functionCount <= 8)
        {
            return 0;
        }

        int viewportHeight = GetViewportHeight();
        if (viewportHeight < 20)
        {
            return 0;
        }

        int rowBudget = Math.Clamp((viewportHeight / 3) - 3, 3, 8);
        return functionCount <= rowBudget * 3
            ? Math.Min(functionCount, rowBudget)
            : 0;
    }

    private static int GetFunctionHeaderPriority(FunctionInfo function) => function.Status switch
    {
        FunctionStatus.Active => 0,
        FunctionStatus.Error => 1,
        _ when function.LastInvocationAt is not null => 2,
        _ => 3,
    };

    private static FunctionInfo[] GetFunctionSearchMatches(FunctionInfo[] functions, string query)
    {
        string trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            return functions;
        }

        return [.. functions.Where(fn =>
            IsFuzzyMatch(fn.Name, trimmed)
            || IsFuzzyMatch(fn.TriggerType, trimmed)
            || IsFuzzyMatch(fn.Route, trimmed))];
    }

    private static bool IsFuzzyMatch(string? value, string query)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        int queryIndex = 0;
        foreach (char valueChar in value)
        {
            if (char.ToUpperInvariant(valueChar) == char.ToUpperInvariant(query[queryIndex]))
            {
                queryIndex++;
                if (queryIndex == query.Length)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int GetFunctionBrowserTotalRows(int functionCount)
        => Math.Max(1, (functionCount + 1) / 2);

    private static int GetFunctionBrowserRow(int index, int totalRows)
        => totalRows <= 0 ? 0 : index % totalRows;

    private int GetFunctionBrowserVisibleRows(int functionCount)
    {
        int totalRows = GetFunctionBrowserTotalRows(functionCount);
        int viewportHeight = GetViewportHeight();

        // Reserve: logs rule (1), minimum log tail (3), safety pad (2), and
        // browser panel chrome/spacer/footer (4). On a 24-row terminal this
        // leaves room for about 14 grid rows, which shows up to 28 functions
        // in the two-column browser before scrolling is needed.
        int maxRows = Math.Max(4, viewportHeight - 10);
        return Math.Min(totalRows, maxRows);
    }

    private int GetFunctionSearchVisibleRows(int matchCount)
    {
        int viewportHeight = GetViewportHeight();
        int maxRows = Math.Max(3, viewportHeight - 14);
        return Math.Min(matchCount, maxRows);
    }

    private int GetViewportHeight()
    {
        int viewportHeight = _console.Profile.Height;
        if (viewportHeight <= 0 || viewportHeight > 1000)
        {
            viewportHeight = 30;
        }

        return viewportHeight;
    }

    private static string? GetFunctionName(HostLogEntry entry, IReadOnlyList<DashboardEvent> events)
    {
        foreach (DashboardEvent ev in events)
        {
            switch (ev)
            {
                case FunctionDiscoveredEvent fd:
                    return fd.Function.Name;
                case InvocationStartedEvent started:
                    return started.Function;
                case InvocationCompletedEvent completed:
                    return completed.Function;
            }
        }

        return entry.GetAttribute<string>(HostLogAttributeKeys.FunctionName);
    }

    private static string GetEmptyLogMessage(string? activeFunctionFilter)
        => activeFunctionFilter is null
            ? "Waiting for events…"
            : $"No logs for {activeFunctionFilter} yet…";

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

    private static bool IsErrorLogLine(HostLogEntry entry, IReadOnlyList<DashboardEvent> events)
    {
        if (entry.Level is LogLevel.Error or LogLevel.Critical)
        {
            return true;
        }

        return events.Any(static ev =>
            ev is InvocationCompletedEvent { Result: var result }
            && string.Equals(result, "failed", StringComparison.OrdinalIgnoreCase));
    }

    private static LogLevel GetEffectiveLogLevel(HostLogEntry entry, bool isError)
        => isError && entry.Level < LogLevel.Error
            ? LogLevel.Error
            : entry.Level;

    private static bool MatchesLogFilters(
        LogLine line,
        string? activeFunctionFilter,
        bool errorsOnly,
        LogLevel minimumLogLevel)
    {
        if (activeFunctionFilter is not null
            && line.FunctionName is not null
            && !string.Equals(line.FunctionName, activeFunctionFilter, StringComparison.Ordinal))
        {
            return false;
        }

        if (errorsOnly && !line.IsError)
        {
            return false;
        }

        return line.Level >= minimumLogLevel;
    }

    private static string FormatMinimumLogLevel(LogLevel minimumLogLevel) => minimumLogLevel switch
    {
        LogLevel.Warning => "warn",
        LogLevel.Error or LogLevel.Critical => "error",
        _ => "info",
    };

    private sealed record LogLine(IRenderable Renderable, string? FunctionName, bool IsError, LogLevel Level);

    private sealed class FunctionNameComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (string.Equals(x, y, StringComparison.Ordinal))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            (string XPrefix, int? XNumber) = SplitTrailingNumber(x);
            (string YPrefix, int? YNumber) = SplitTrailingNumber(y);

            int prefixComparison = string.Compare(XPrefix, YPrefix, StringComparison.Ordinal);
            if (prefixComparison != 0)
            {
                return prefixComparison;
            }

            if (XNumber is { } xn && YNumber is { } yn)
            {
                int numberComparison = xn.CompareTo(yn);
                if (numberComparison != 0)
                {
                    return numberComparison;
                }
            }

            return string.Compare(x, y, StringComparison.Ordinal);
        }

        private static (string Prefix, int? Number) SplitTrailingNumber(string value)
        {
            int digitStart = value.Length;
            while (digitStart > 0 && char.IsDigit(value[digitStart - 1]))
            {
                digitStart--;
            }

            if (digitStart == value.Length)
            {
                return (value, null);
            }

            string suffix = value[digitStart..];
            return int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
                ? (value[..digitStart], number)
                : (value, null);
        }
    }
}
