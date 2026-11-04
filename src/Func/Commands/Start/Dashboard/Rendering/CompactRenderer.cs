// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Clipboard;
using Azure.Functions.Cli.Console.Terminal;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Full-screen dashboard with an anchored log document and exclusive terminal ownership.
/// </summary>
internal sealed class CompactRenderer(
    IInteractionService interaction,
    FunctionPalette palette,
    CompactDashboardShortcutLabels shortcutLabels,
    IAnsiConsole? console = null,
    DashboardRunInfo? runInfo = null,
    ITerminalSessionFactory? terminalFactory = null,
    IClipboardWriter? clipboard = null,
    ConsoleInterruptCoordinator? interrupts = null,
    ITerminalSession? terminal = null,
    TimeProvider? timeProvider = null) : IDashboardRenderer, IDashboardShutdownRequester, IDashboardRendererLifetime
{
    private const int MaxLogTailLines = CompactLogBuffer.DefaultCapacity;

    private static readonly IComparer<string> _functionNameComparer = new FunctionNameComparer();

    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly FunctionPalette _palette = palette ?? throw new ArgumentNullException(nameof(palette));
    private readonly IAnsiConsole? _console = console;
    private readonly ITerminalSessionFactory? _terminalFactory = terminalFactory;
    private readonly IClipboardWriter? _clipboard = clipboard;
    private readonly ConsoleInterruptCoordinator? _interrupts = interrupts;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly CompactHeaderBuilder _headerBuilder = new(interaction.Theme, runInfo ?? new());
    private readonly CompactFooterBuilder _footerBuilder = new(interaction.Theme, runInfo ?? new(), shortcutLabels);
    private readonly CompactHelpOverlayBuilder _helpOverlayBuilder = new(interaction.Theme, shortcutLabels);
    private readonly CompactFunctionSearchBuilder _functionSearchBuilder = new(interaction.Theme, palette);
    private readonly CompactFunctionBrowserBuilder _functionBrowserBuilder = new(interaction.Theme, palette);
    private readonly Lock _uiLock = new();
    private readonly CompactLogBuffer _logBuffer = new(MaxLogTailLines);
    private readonly CompactLogLineFormatter _logLineFormatter = new(interaction.Theme, palette);
    private readonly LogViewport _viewport = new();
    private readonly List<TerminalInputEvent> _deferredInput = [];
    private ITerminalSession? _terminal = terminal;
    private IDisposable? _interruptRegistration;
    private CancellationTokenRegistration _cancellationRegistration;
    private Task? _completion;
    private Task? _copyTask;
    private CancellationTokenSource? _copyCts;
    private int _dirty = 1;
    private bool _disposed;
    private bool _stopped;
    private bool _stopping;
    private bool _mouseCapture = true;
    private bool _manualRedraw = true;
    private bool _painting;
    private TerminalSize? _frameSize;
    private int _logTop;
    private int _logHeight;
    private int _dragX;
    private int _dragY;
    private string? _status;
    private int? _hostExitCode;

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

    private string MutedTag => field ??= Theme.Muted.ToMarkup();
    private string EmphasisTag => field ??= Theme.Emphasis.ToMarkup();
    private string SuccessTag => field ??= Theme.Success.ToMarkup();
    private string ErrorTag => field ??= Theme.Error.ToMarkup();
    private string ActiveTag => field ??= Theme.Active.ToMarkup();

    private CompactInputController InputController => field ??= new(_functionSearchBuilder, _functionBrowserBuilder);

    public event Action? ShutdownRequested;

    public Task Completion => _completion ?? Task.CompletedTask;

    public Task OnStartAsync(DashboardState state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _terminal ??= _interaction.OpenFullScreen(_terminalFactory
            ?? throw new InvalidOperationException("A terminal session factory is required."), cancellationToken);
        _interruptRegistration = _interrupts?.RegisterTerminal(_terminal.Restore);
        _redrawSignal = new SemaphoreSlim(initialCount: 1, maxCount: 1);
        // Keep raw Ctrl+C available for escalation while the host drains and shuts down.
        _liveCts = new CancellationTokenSource();
        _cancellationRegistration = cancellationToken.Register(() => BeginShutdown());
        _liveTask = Task.Run(() => RunLiveLoopAsync(_liveCts.Token));
        _inputTask = Task.Run(() => RunInputLoopAsync(_liveCts.Token));
        _completion = MonitorAsync(_liveTask, _inputTask, _liveCts.Token);
        return Task.CompletedTask;
    }

    public void BeginShutdown(int? hostExitCode = null)
    {
        lock (_uiLock)
        {
            _stopping = true;
            _hostExitCode = hostExitCode ?? _hostExitCode;
            _manualRedraw = true;
            _viewport.ClearSelection();
            _copyCts?.Cancel();
            _status = "Stopping host; Ctrl+C forces exit";
            _interrupts?.MarkStopping();
        }

        SignalRedraw();
    }

    public Task OnEventAsync(HostLogEntry entry, IReadOnlyList<DashboardEvent> events, CancellationToken cancellationToken)
    {
        CompactLogLine? line = _logLineFormatter.Format(entry, events, GetListenUriForLogFormatting(events));
        if (line is null)
        {
            return Task.CompletedTask;
        }

        _logBuffer.Add(line);

        SignalRedraw();
        return Task.CompletedTask;
    }

    public async Task OnSummaryAsync(SummaryEvent summary, CancellationToken cancellationToken)
    {
        await StopAsync();

        var uptime = TimeSpan.FromSeconds(summary.UptimeSeconds);
        _interaction.WriteBlankLine();
        _interaction.WriteLine($"  Azure Functions host stopped after {uptime.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)}");
        _interaction.WriteLine($"  {summary.FunctionCount} functions  ·  {summary.TotalInvocations} invocations  ·  {summary.ErrorCount} error{(summary.ErrorCount == 1 ? string.Empty : "s")}");
        if (_hostExitCode is not null and not 0)
        {
            _interaction.WriteError($"The host exited with code {_hostExitCode.Value}.");
        }
        CompactLogLine? failure = _logBuffer.Snapshot().LastOrDefault(line => line.IsError);
        if (failure is not null)
        {
            _interaction.WriteLine("  Last error:");
            using StringReader errorText = new(failure.Text);
            for (int index = 0; index < 8 && errorText.ReadLine() is { } line; index++)
            {
                _interaction.WriteRawLine(line.Length <= 500 ? line
                    : line[..LogText.BoundaryBefore(line, 500)] + " [display truncated]");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await StopAsync();
        }
        finally
        {
            _cancellationRegistration.Dispose();
            _interruptRegistration?.Dispose();
            _copyCts?.Dispose();
            _liveCts?.Dispose();
            _redrawSignal?.Dispose();
        }
    }

    private async Task StopAsync()
    {
        if (_stopped)
        {
            return;
        }

        _liveCts?.Cancel();
        var workers = Task.WhenAll(_liveTask ?? Task.CompletedTask, _inputTask ?? Task.CompletedTask,
            _copyTask ?? Task.CompletedTask, _completion ?? Task.CompletedTask);
        try
        {
            await workers.WaitAsync(TimeSpan.FromSeconds(2), _timeProvider, CancellationToken.None);
        }
        catch (OperationCanceledException) when (_liveCts?.IsCancellationRequested == true)
        {
            // The owned input and rendering tasks have joined.
        }
        catch (TimeoutException exception)
        {
            // Restore input even when a broken terminal transport has stalled a synchronous writer.
            _terminal?.Restore();
            _ = workers.ContinueWith(task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            throw new IOException("Terminal I/O did not stop during shutdown. Terminal restoration was attempted.", exception);
        }
        finally
        {
            try
            {
                if (_terminal is not null)
                {
                    await _terminal.DisposeAsync();
                }
            }
            finally
            {
                _stopped = true;
            }
        }
    }

    private static async Task MonitorAsync(Task rendering, Task input, CancellationToken cancellationToken)
    {
        Task completed = await Task.WhenAny(rendering, input);
        await completed;
        if (!cancellationToken.IsCancellationRequested)
        {
            throw new EndOfStreamException("The terminal session ended unexpectedly.");
        }
    }

    private void SignalRedraw()
    {
        try
        {
            if (Interlocked.Exchange(ref _dirty, 1) == 0)
            {
                _redrawSignal?.Release();
            }
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
        await foreach (TerminalInputEvent input in _terminal!.ReadAsync(cancellationToken))
        {
            lock (_uiLock)
            {
                bool interrupt = input.Kind == TerminalInputKind.Key && input.Key.KeyChar == '\u0003';
                bool canStopImmediately = interrupt && (_stopping
                    || (!_viewport.HasSelection && !_viewport.SelectionExpired
                        && !_deferredInput.Any(item => item.Kind is TerminalInputKind.PointerDown or TerminalInputKind.PointerMove)));
                if (_painting && !canStopImmediately)
                {
                    if (input.Kind == TerminalInputKind.PointerMove && _deferredInput.Count > 0
                        && _deferredInput[^1].Kind == TerminalInputKind.PointerMove)
                    {
                        _deferredInput[^1] = input;
                    }
                    else
                    {
                        if (_deferredInput.Count == 256)
                        {
                            throw new IOException("Terminal output is not keeping up with input. Use --format=plain.");
                        }

                        _deferredInput.Add(input);
                    }
                }
                else
                {
                    HandleInput(input, cancellationToken);
                }

                _manualRedraw |= _mouseCapture || input.Kind is TerminalInputKind.Key or TerminalInputKind.Text
                    or TerminalInputKind.Paste or TerminalInputKind.Resize;
            }

            SignalRedraw();
        }
    }

    private void HandleInput(TerminalInputEvent input, CancellationToken cancellationToken)
    {
        if (input.Kind == TerminalInputKind.Key && (input.Key.KeyChar == '\u0003'
            || input.Key.Key == ConsoleKey.C && input.Key.Modifiers.HasFlag(ConsoleModifiers.Control)))
        {
            RefreshViewport(_state.Snapshot());
            if (!_stopping && (_viewport.HasSelection || _viewport.SelectionExpired))
            {
                if (_viewport.SelectionExpired)
                {
                    _status = "Selection expired; select again or Esc to clear";
                }
                else if (_copyTask is null || _copyTask.IsCompleted)
                {
                    LogCopySnapshot copy = _viewport.CaptureSelection();
                    _status = "Copying selection...";
                    _copyCts?.Dispose();
                    _copyCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    CancellationToken copyToken = _copyCts.Token;
                    _copyTask = Task.Run(() => CopyAsync(copy, copyToken));
                }
            }
            else if (_interrupts is not null)
            {
                _interrupts.RequestStop();
            }
            else
            {
                ShutdownRequested?.Invoke();
            }

            return;
        }

        if (_stopping)
        {
            return;
        }

        if (input.Kind is TerminalInputKind.Text or TerminalInputKind.Paste)
        {
            if (_functionSearchOpen && input.Text is not null)
            {
                _functionSearchQuery = (_functionSearchQuery + LogText.Sanitize(input.Text).Replace('\n', ' '));
                if (_functionSearchQuery.Length > 4096)
                {
                    _functionSearchQuery = _functionSearchQuery[..LogText.BoundaryBefore(_functionSearchQuery, 4096)];
                    _status = "Search text limited to 4096 characters";
                }
            }

            return;
        }

        if (input.Kind == TerminalInputKind.Key)
        {
            if (!_functionSearchOpen && input.Key.Key == ConsoleKey.M && input.Key.Modifiers == 0)
            {
                _mouseCapture = !_mouseCapture;
                _terminal!.SetMouseCapture(_mouseCapture);
                _viewport.ClearSelection();
                _viewport.Pause();
                _status = _mouseCapture ? null : "Mouse off; display paused; m resumes";
            }
            else if (!_functionSearchOpen && !_helpOpen && !_functionBrowserOpen
                && input.Key.Key == ConsoleKey.Escape && (_viewport.HasSelection || _viewport.SelectionExpired))
            {
                _viewport.ClearSelection();
                _status = null;
            }
            else
            {
                HandleKey(input.Key);
            }

            return;
        }

        if (input.Kind is TerminalInputKind.FocusLost or TerminalInputKind.Resize)
        {
            _viewport.EndDrag();
            return;
        }

        if (_frameSize is { } presentedSize && _terminal is not null && _terminal.Size != presentedSize)
        {
            _viewport.EndDrag();
            return;
        }

        if (!_mouseCapture || _helpOpen || _functionBrowserOpen || _functionSearchOpen || _logHeight == 0)
        {
            return;
        }

        int y = input.Y - _logTop;
        bool inside = y >= 0 && y < _logHeight && input.X >= 0 && input.X < GetViewportWidth();
        switch (input.Kind)
        {
            case TerminalInputKind.Wheel when inside:
                _viewport.Scroll(input.Delta);
                break;
            case TerminalInputKind.PointerDown when inside:
                _status = null;
                _dragX = input.X;
                _dragY = y;
                _viewport.BeginDrag(input.X, y);
                break;
            case TerminalInputKind.PointerMove:
                _dragX = input.X;
                _dragY = y;
                _viewport.Drag(input.X, y);
                break;
            case TerminalInputKind.PointerUp:
                _viewport.EndDrag();
                break;
        }
    }

    private async Task CopyAsync(LogCopySnapshot copy, CancellationToken cancellationToken)
    {
        try
        {
            ClipboardCopyResult result = _clipboard is null
                ? new ClipboardCopyResult(ClipboardCopyStatus.Failed, "Clipboard access is unavailable; use m for native selection")
                : await _clipboard.CopyAsync(copy.Text, _terminal!, cancellationToken);
            lock (_uiLock)
            {
                _viewport.CompleteCopy(copy.SelectionVersion, result.Status == ClipboardCopyStatus.Confirmed);
                _status = result.Message;
                _manualRedraw = true;
            }

            SignalRedraw();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Closing the session cancels in-flight clipboard work.
        }
    }

    private bool HandleKey(ConsoleKeyInfo key)
    {
        DashboardSnapshot snapshot = _state.Snapshot();
        FunctionInfo[] functions = GetSortedFunctions(snapshot);

        lock (_uiLock)
        {
            RefreshViewport(snapshot);
            bool overlays = _functionSearchOpen || _functionBrowserOpen || _helpOpen;
            if (!overlays && key.Key == ConsoleKey.End)
            {
                _viewport.Follow();
                _status = null;
                _logScrollOffset = 0;
                return true;
            }

            CompactInputState state = CaptureInputState();
            bool choosingFilter = (state.FunctionSearchOpen || state.FunctionBrowserOpen) && key.Key == ConsoleKey.Enter;
            int previousOffset = _viewport.ScrollOffset;
            state.LogScrollOffset = previousOffset;
            CompactInputResult result = InputController.HandleKey(
                key,
                functions,
                state,
                GetViewportHeight(),
                Math.Max(1, _viewport.Height - 1),
                _viewport.MaximumScroll);

            ApplyInputState(state);
            if (choosingFilter && result.Handled && !state.FunctionSearchOpen && !state.FunctionBrowserOpen)
            {
                _viewport.Follow();
            }
            if (result.Message is not null)
            {
                _status = result.Message;
            }
            if (!overlays && state.LogScrollOffset != previousOffset)
            {
                _viewport.Scroll(state.LogScrollOffset - previousOffset);
            }

            if (_helpOpen || _functionBrowserOpen || _functionSearchOpen)
            {
                _viewport.ClearSelection();
            }

            if (result.ClearLogsRequested)
            {
                _logBuffer.Clear();
                _viewport.Follow();
            }

            if (result.ShutdownRequested)
            {
                ShutdownRequested?.Invoke();
            }

            return result.Handled;
        }
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
        while (!cancellationToken.IsCancellationRequested)
        {
            bool changed = await _redrawSignal!.WaitAsync(TimeSpan.FromMilliseconds(100), cancellationToken);
            Interlocked.Exchange(ref _dirty, 0);
            IRenderable? frame = null;
            Task? copy;
            lock (_uiLock)
            {
                copy = _copyTask;
                TerminalSize size = _terminal!.Size;
                int width = size.Width;
                int height = size.Height;
                bool resized = width != _lastKnownWidth || height != _lastKnownHeight;
                if (resized)
                {
                    _viewport.EndDrag();
                }

                bool autoScroll = _viewport.IsDragging && (_dragY < 0 || _dragY >= _logHeight);
                if (autoScroll)
                {
                    _viewport.Scroll(_dragY < 0 ? 1 : -1);
                    _viewport.Drag(_dragX, _dragY);
                }

                if ((_mouseCapture || _manualRedraw || resized) && (changed || resized || autoScroll))
                {
                    frame = BuildLayout();
                    _painting = true;
                    _manualRedraw = false;
                    _lastKnownWidth = width;
                    _lastKnownHeight = height;
                }
            }

            if (copy?.IsFaulted == true)
            {
                await copy;
            }

            if (frame is not null)
            {
                try
                {
                    _terminal!.Render(frame);
                }
                finally
                {
                    lock (_uiLock)
                    {
                        _painting = false;
                        if (_deferredInput.Count > 0)
                        {
                            foreach (TerminalInputEvent input in _deferredInput)
                            {
                                if (!cancellationToken.IsCancellationRequested)
                                {
                                    HandleInput(input, cancellationToken);
                                }
                            }

                            _deferredInput.Clear();
                            _manualRedraw = true;
                            SignalRedraw();
                        }
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(33), _timeProvider, cancellationToken);
            }
        }
    }

    private IRenderable BuildLayout()
    {
        DashboardSnapshot snapshot = _state.Snapshot();
        lock (_uiLock)
        {
            _frameSize = _terminal?.Size ?? new TerminalSize(_console?.Profile.Width ?? 120, _console?.Profile.Height ?? 30);
            if (_frameSize.Value.Width <= 0 || _frameSize.Value.Height <= 0)
            {
                throw new IOException("The terminal viewport is unavailable.");
            }

            IRenderable header = GetViewportWidth() < 24 || GetViewportHeight() < 6
                ? new Text("Resize terminal", Theme.Muted)
                : BuildHeader(snapshot);
            RefreshViewport(snapshot, header);
            IReadOnlyList<IRenderable> rows = _viewport.RenderRows(Theme.Selection);
            if (rows.Count == 0)
            {
                rows = [new Text(LogText.Sanitize(GetEmptyLogMessage(_activeFunctionFilter)), Theme.Muted)];
            }

            return new DashboardFrame(GetViewportWidth(), GetViewportHeight(), header, _logTop - 1, rows,
                BuildFooterCore(snapshot, _activeFunctionFilter, _errorsOnly, _minimumLogLevel), Theme.Muted);
        }
    }

    private void RefreshViewport(DashboardSnapshot snapshot, IRenderable? header = null)
    {
        header ??= BuildHeader(snapshot);
        RenderOptions options = LogText.CreateRenderOptions(GetViewportWidth(), GetViewportHeight());
        int headerHeight = Segment.SplitLines(header.Render(options, GetViewportWidth())).Count;
        headerHeight = Math.Clamp(headerHeight, 0, Math.Max(0, GetViewportHeight() - 3));
        if (GetViewportWidth() < 24 || GetViewportHeight() < 6)
        {
            headerHeight = Math.Min(1, Math.Max(0, GetViewportHeight() - 2));
        }

        _logTop = headerHeight + 1;
        _logHeight = Math.Max(0, GetViewportHeight() - headerHeight - 2);
        _viewport.Refresh(_logBuffer.Capture(), GetViewportWidth(), Math.Max(1, _logHeight),
            new LogFilter(_activeFunctionFilter, _errorsOnly, _minimumLogLevel));
        _logScrollOffset = _viewport.ScrollOffset;
    }

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
            _ when GetViewportWidth() < 64 || GetViewportHeight() < 16 => BuildFunctionsStatusStrip(snapshot),
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
        int nameWidth = Math.Clamp(GetViewportWidth() / 4, 12, 30);
        int statusWidth = Math.Clamp(GetViewportWidth() / 8, 7, 12);
        const int TriggerWidth = 7;
        int routeWidth = Math.Max(1, GetViewportWidth() - nameWidth - statusWidth - TriggerWidth - 10);
        Table table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn(string.Empty) { Width = nameWidth }.PadLeft(2).PadRight(2).NoWrap())
            .AddColumn(new TableColumn(string.Empty) { Width = TriggerWidth }.PadLeft(0).PadRight(2).NoWrap())
            .AddColumn(new TableColumn(string.Empty) { Width = routeWidth }.PadLeft(0).PadRight(2).NoWrap())
            .AddColumn(new TableColumn(string.Empty) { Width = statusWidth }.PadLeft(0).PadRight(0).NoWrap());

        table.AddRow(
            new Markup($"[{EmphasisTag}]Function[/]"),
            new Markup($"[{EmphasisTag}]Trigger[/]"),
            new Markup($"[{EmphasisTag}]Route / Source[/]"),
            new Markup($"[{EmphasisTag}]Status[/]"));

        foreach (FunctionInfo fn in functions)
        {
            string color = _palette.GetColorFor(fn.Name);
            string routeMarkup = HttpRouteFormatter.FormatRouteMarkup(fn, listenUri, Theme, routeWidth);

            table.AddRow(
                new Markup($"[{color}]{LogText.EscapeMarkup(LogText.Clip(fn.Name, nameWidth))}[/]"),
                new Markup(LogText.EscapeMarkup(LogText.Clip(FormatTrigger(fn.TriggerType), TriggerWidth))),
                new Markup($"[{MutedTag}]{routeMarkup}[/]"),
                new Markup(FormatStatus(fn)) { Overflow = Overflow.Ellipsis });
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

        if (GetViewportWidth() < 60)
        {
            string text = _functionSearchOpen || _functionBrowserOpen || _helpOpen
                ? "Esc close | Ctrl+C stop"
                : _stopping ? "Stopping; Ctrl+C force exit"
                : _viewport.SelectionExpired ? "q quit | Selection expired; Esc clear"
                : _viewport.HasSelection ? $"q quit | {_status ?? "Ctrl+C copy | Esc clear"}"
                : $"q quit | ? help | {_status ?? (_viewport.Following ? "Live" : "End live")}";
            return new Text(text, Theme.Muted);
        }

        return _footerBuilder.Build(
            snapshot,
            activeFunctionFilter,
            errorsOnly,
            minimumLogLevel,
            logScrollOffset,
            helpOpen,
            functionSearchOpen,
            functionBrowserOpen,
            _stopping ? "Stopping; Ctrl+C force exit"
                : _viewport.SelectionExpired ? "Selection expired; Esc clear"
                : _viewport.HasSelection ? $"Ctrl+C copy | Esc clear{(_status is null ? string.Empty : " | " + _status)}"
                : _status ?? (_viewport.Following ? "Live"
                    : $"Scrollback | {_viewport.UnreadEntries} new | End live")
                    + (_viewport.Evicted > 0 ? " | older history discarded" : string.Empty),
            selectionActive: _viewport.HasSelection || _viewport.SelectionExpired);
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
        if (_frameSize is { } frame)
        {
            return frame.Height;
        }

        if (_terminal is not null)
        {
            int height = _terminal.Size.Height;
            return height > 0 ? height : throw new IOException("The terminal viewport height is unavailable.");
        }

        int viewportHeight = _console?.Profile.Height ?? 30;
        if (viewportHeight <= 0 || viewportHeight > 1000)
        {
            viewportHeight = 30;
        }

        return viewportHeight;
    }

    private int GetViewportWidth()
    {
        if (_frameSize is { } frame)
        {
            return frame.Width;
        }

        if (_terminal is not null)
        {
            int width = _terminal.Size.Width;
            return width > 0 ? width : throw new IOException("The terminal viewport width is unavailable.");
        }

        int viewportWidth = _console?.Profile.Width ?? 120;
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
