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
    CompactDashboardShortcutLabels shortcutLabels,
    IPlatform platform,
    IAnsiConsole? console = null,
    DashboardRunInfo? runInfo = null) : IDashboardRenderer, IDashboardShutdownRequester
{
    private const int MaxLogTailLines = CompactLogBuffer.DefaultCapacity;
    private const int HelpOverlayCommandRows = 15;
    private const int HelpOverlayLines = HelpOverlayCommandRows + 3;
    private const string EnterAlternateScreenSequence = "\u001b[?1049h\u001b[H";
    private const string ExitAlternateScreenSequence = "\u001b[?1049l";

    private static readonly IComparer<string> _functionNameComparer = new FunctionNameComparer();

    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly FunctionPalette _palette = palette ?? throw new ArgumentNullException(nameof(palette));
    private readonly IPlatform _platform = platform ?? throw new ArgumentNullException(nameof(platform));
    private readonly IAnsiConsole _console = console ?? AnsiConsole.Console;
    private readonly CompactHeaderBuilder _headerBuilder = new(interaction.Theme, runInfo ?? new());
    private readonly CompactFooterBuilder _footerBuilder = new(interaction.Theme, runInfo ?? new(), shortcutLabels);
    private readonly CompactHelpOverlayBuilder _helpOverlayBuilder = new(interaction.Theme, shortcutLabels);
    private readonly CompactFunctionSearchBuilder _functionSearchBuilder = new(interaction.Theme, palette);
    private readonly CompactFunctionBrowserBuilder _functionBrowserBuilder = new(interaction.Theme, palette);
    private readonly Lock _uiLock = new();
    private readonly CompactLogBuffer _logBuffer = new(MaxLogTailLines);
    private readonly CompactLogLineFormatter _logLineFormatter = new(interaction.Theme, palette);

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
    private string MutedTag => field ??= Theme.Muted.ToMarkup();
    private string EmphasisTag => field ??= Theme.Emphasis.ToMarkup();
    private string SuccessTag => field ??= Theme.Success.ToMarkup();
    private string ErrorTag => field ??= Theme.Error.ToMarkup();
    private string WarningTag => field ??= Theme.Warning.ToMarkup();
    private string ActiveTag => field ??= Theme.Active.ToMarkup();

    private CompactInputController InputController => field ??= new(_functionSearchBuilder, _functionBrowserBuilder);

    public event Action? ShutdownRequested;

    public Task OnStartAsync(DashboardState state, CancellationToken cancellationToken)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _redrawSignal = new SemaphoreSlim(initialCount: 1, maxCount: int.MaxValue);
        _liveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _console.Cursor.Hide();
        _liveTask = Task.Run(() => RunLiveLoopInTerminalModeAsync(_liveCts.Token));

        if (_interaction.IsInteractive)
        {
            _inputTask = Task.Run(() => RunInputLoopAsync(_liveCts.Token), cancellationToken);
        }

        return Task.CompletedTask;
    }

    public Task OnEventAsync(HostLogEntry entry, IReadOnlyList<DashboardEvent> events, CancellationToken cancellationToken)
    {
        CompactLogLine? line = _logLineFormatter.Format(entry, events, GetListenUriForLogFormatting(events));
        if (line is null)
        {
            return Task.CompletedTask;
        }

        _logBuffer.Add(line);

        lock (_uiLock)
        {
            if (_logScrollOffset > 0 && MatchesLogFilters(line, _activeFunctionFilter, _errorsOnly, _minimumLogLevel))
            {
                int width = GetViewportWidth();
                int addedRows = line.RenderRows(width).Count;

                // Account for the blank separator row that BuildLogVisualRows inserts
                // before a multi-line entry so a held scrollback position stays anchored.
                if (SeparatorPrecedesNewEntry(line, width))
                {
                    addedRows++;
                }

                _logScrollOffset = Math.Min(MaxLogTailLines, _logScrollOffset + addedRows);
            }
        }

        SignalRedraw();
        return Task.CompletedTask;
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

    private string? GetListenUriForLogFormatting(IReadOnlyList<DashboardEvent> events)
        => events.Any(static ev => ev is FunctionDiscoveredEvent) && _state is not null
            ? _state.Snapshot().ListenUri
            : null;

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
            CompactInputState state = CaptureInputState();
            CompactInputResult result = InputController.HandleKey(
                key,
                functions,
                state,
                GetViewportHeight(),
                GetLogScrollStep(snapshot),
                MaxLogTailLines);

            ApplyInputState(state);
            if (result.ClearLogsRequested)
            {
                _logBuffer.Clear();
            }

            if (result.ShutdownRequested)
            {
                ShutdownRequested?.Invoke();
            }

            return result.Handled;
        }
    }

    private int GetLogScrollStep(DashboardSnapshot snapshot)
    {
        int logBudget = ComputeLogBudgetCore(snapshot, _helpOpen, functionSearchOpen: false, functionBrowserOpen: false, searchQuery: string.Empty);
        return Math.Max(1, logBudget - 1);
    }

    private CompactInputState CaptureInputState()
    {
        return new CompactInputState
        {
            HelpOpen = _helpOpen,
            FunctionBrowserOpen = _functionBrowserOpen,
            FunctionSearchOpen = _functionSearchOpen,
            FunctionBrowserSelectedIndex = _functionBrowserSelectedIndex,
            FunctionBrowserRowOffset = _functionBrowserRowOffset,
            FunctionSearchSelectedIndex = _functionSearchSelectedIndex,
            FunctionSearchRowOffset = _functionSearchRowOffset,
            FunctionSearchQuery = _functionSearchQuery,
            ActiveFunctionFilter = _activeFunctionFilter,
            LogScrollOffset = _logScrollOffset,
            ErrorsOnly = _errorsOnly,
            MinimumLogLevel = _minimumLogLevel,
        };
    }

    private void ApplyInputState(CompactInputState state)
    {
        _helpOpen = state.HelpOpen;
        _functionBrowserOpen = state.FunctionBrowserOpen;
        _functionSearchOpen = state.FunctionSearchOpen;
        _functionBrowserSelectedIndex = state.FunctionBrowserSelectedIndex;
        _functionBrowserRowOffset = state.FunctionBrowserRowOffset;
        _functionSearchSelectedIndex = state.FunctionSearchSelectedIndex;
        _functionSearchRowOffset = state.FunctionSearchRowOffset;
        _functionSearchQuery = state.FunctionSearchQuery;
        _activeFunctionFilter = state.ActiveFunctionFilter;
        _logScrollOffset = state.LogScrollOffset;
        _errorsOnly = state.ErrorsOnly;
        _minimumLogLevel = state.MinimumLogLevel;
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

    private async Task RunLiveLoopInTerminalModeAsync(CancellationToken cancellationToken)
    {
        if (!ShouldUseAlternateScreen())
        {
            await RunLiveLoopAsync(cancellationToken);
            return;
        }

        _console.Write(new ControlCode(EnterAlternateScreenSequence));
        _console.Cursor.Hide();

        try
        {
            await RunLiveLoopAsync(cancellationToken);
        }
        finally
        {
            _console.Write(new ControlCode(ExitAlternateScreenSequence));
        }
    }

    private bool ShouldUseAlternateScreen()
        => _platform.IsMacOS && _console.Profile.Capabilities.Ansi && _console.Profile.Capabilities.AlternateBuffer;

    private IRenderable BuildLayout()
    {
        DashboardSnapshot snapshot = _state.Snapshot();
        IRenderable header = BuildHeader(snapshot);

        CompactLogLine[] tail = _logBuffer.Snapshot();

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
        List<IRenderable> visualRows = BuildLogVisualRows(tail, activeFunctionFilter, GetViewportWidth());
        int logScrollOffset;
        lock (_uiLock)
        {
            _logScrollOffset = Math.Clamp(_logScrollOffset, 0, Math.Max(0, visualRows.Count - logBudget));
            logScrollOffset = _logScrollOffset;
        }

        if (visualRows.Count > logBudget)
        {
            int start = Math.Max(0, visualRows.Count - logBudget - logScrollOffset);
            visualRows = visualRows[start..Math.Min(visualRows.Count, start + logBudget)];
        }

        IRenderable logRows = BuildLogRows(visualRows, logBudget);
        IRenderable footer = BuildFooterCore(snapshot, activeFunctionFilter, errorsOnly, minimumLogLevel);

        var rows = new Rows(
            header,
            new Rule("logs").LeftJustified().RuleStyle(Theme.Muted),
            logRows,
            footer);

        return rows;
    }

    private List<IRenderable> BuildLogVisualRows(CompactLogLine[] tail, string? activeFunctionFilter, int viewportWidth)
    {
        if (tail.Length == 0)
        {
            return [new Markup($"[{MutedTag}]{Markup.Escape(GetEmptyLogMessage(activeFunctionFilter))}[/]")];
        }

        var rows = new List<IRenderable>();
        bool previousWasMultiline = false;
        for (int i = 0; i < tail.Length; i++)
        {
            IReadOnlyList<IRenderable> lineRows = tail[i].RenderRows(viewportWidth);
            bool multiline = lineRows.Count > 1;

            // Only separate multi-line entries (wrapped messages or exception
            // summaries) so they read as a distinct block. Consecutive single-line
            // entries stay dense to keep the live tail compact. A single space (not
            // string.Empty) is required: Spectre's Rows collapses an empty Markup.
            if (i > 0 && (previousWasMultiline || multiline))
            {
                rows.Add(new Markup(" "));
            }

            rows.AddRange(lineRows);
            previousWasMultiline = multiline;
        }

        return rows;
    }

    private static bool IsMultiline(CompactLogLine line, int viewportWidth) => line.RenderRows(viewportWidth).Count > 1;

    private bool SeparatorPrecedesNewEntry(CompactLogLine current, int viewportWidth)
    {
        CompactLogLine[] snapshot = _logBuffer.Snapshot();
        CompactLogLine? previous = null;
        for (int i = snapshot.Length - 2; i >= 0; i--)
        {
            if (MatchesLogFilters(snapshot[i], _activeFunctionFilter, _errorsOnly, _minimumLogLevel))
            {
                previous = snapshot[i];
                break;
            }
        }

        return previous is not null && (IsMultiline(previous, viewportWidth) || IsMultiline(current, viewportWidth));
    }

    private static IRenderable BuildLogRows(List<IRenderable> visualRows, int logBudget)
    {
        if (logBudget <= 0)
        {
            return new Rows([]);
        }

        while (visualRows.Count < logBudget)
        {
            visualRows.Add(new Markup(string.Empty));
        }

        return new Rows(visualRows);
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
            ? _functionSearchBuilder.GetVisibleRows(_functionSearchBuilder.GetMatches(GetSortedFunctions(snapshot), searchQuery).Length, GetViewportHeight()) + CompactFunctionSearchBuilder.ChromeLines
            : functionBrowserOpen
            // Panel border (2) + visible grid rows + spacer/footer (2).
            ? _functionBrowserBuilder.GetVisibleRows(snapshot.Functions.Count, GetViewportHeight()) + CompactFunctionBrowserBuilder.ChromeLines
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

        
        IRenderable bannerPanel = _headerBuilder.BuildBanner(snapshot.HostVersion, snapshot.ListenUri);

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
            string routeMarkup = HttpRouteFormatter.FormatRouteMarkup(fn, listenUri, Theme);

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
        => _helpOverlayBuilder.Build();

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
            matches = _functionSearchBuilder.GetMatches(functions, query);
            visibleRows = _functionSearchBuilder.GetVisibleRows(matches.Length, GetViewportHeight());
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

        return _functionSearchBuilder.Build(query, matches, visibleRows, rowOffset, selectedIndex);
    }

    private IRenderable BuildFunctionBrowser(DashboardSnapshot snapshot)
    {
        FunctionInfo[] functions = GetSortedFunctions(snapshot);
        int totalRows = _functionBrowserBuilder.GetTotalRows(functions.Length);
        int visibleRows = _functionBrowserBuilder.GetVisibleRows(functions.Length, GetViewportHeight());
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
                int selectedRow = _functionBrowserBuilder.GetRow(_functionBrowserSelectedIndex, totalRows);
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

        return _functionBrowserBuilder.Build(functions, totalRows, visibleRows, rowOffset, selectedIndex);
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

    private IRenderable BuildFooter(DashboardSnapshot snapshot, string? activeFunctionFilter)
        => BuildFooterCore(snapshot, activeFunctionFilter, errorsOnly: false, LogLevel.Information);

    private IRenderable BuildFooterCore(DashboardSnapshot snapshot, string? activeFunctionFilter, bool errorsOnly, LogLevel minimumLogLevel)
    {
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

        return _footerBuilder.Build(
            snapshot,
            activeFunctionFilter,
            errorsOnly,
            minimumLogLevel,
            logScrollOffset,
            helpOpen,
            functionSearchOpen,
            functionBrowserOpen);
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

    private int GetViewportHeight()
    {
        int viewportHeight = _console.Profile.Height;
        if (viewportHeight <= 0 || viewportHeight > 1000)
        {
            viewportHeight = 30;
        }

        return viewportHeight;
    }

    private int GetViewportWidth()
    {
        int viewportWidth = _console.Profile.Width;
        if (viewportWidth <= 0 || viewportWidth > 1000)
        {
            viewportWidth = 120;
        }

        return viewportWidth;
    }

    private static string GetEmptyLogMessage(string? activeFunctionFilter)
        => activeFunctionFilter is null
            ? "Waiting for events…"
            : $"No logs for {activeFunctionFilter}…";

    private static bool MatchesLogFilters(CompactLogLine line, string? activeFunctionFilter, bool errorsOnly, LogLevel minimumLogLevel)
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
