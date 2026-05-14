// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Commands.Start.Initialization.Rendering;

/// <summary>
/// Compact initialization renderer shown before the live dashboard starts.
/// </summary>
internal sealed class CompactStartInitializationRenderer(
    IInteractionService interaction,
    IAnsiConsole? console = null,
    DashboardRunInfo? runInfo = null) : IStartInitializationRenderer
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IAnsiConsole _console = console ?? AnsiConsole.Console;
    private readonly Lock _stateLock = new();
    private readonly List<StepState> _steps = [];
    private DashboardRunInfo _runInfo = runInfo ?? new();
    private string? _hostVersion;
    private CancellationTokenSource? _liveCts;
    private SemaphoreSlim? _redrawSignal;
    private Task? _liveTask;

    private ITheme Theme => _interaction.Theme;

    private string MutedTag => field ??= Theme.Muted.ToMarkup();
    private string EmphasisTag => field ??= Theme.Emphasis.ToMarkup();
    private string SuccessTag => field ??= Theme.Success.ToMarkup();
    private string WarningTag => field ??= Theme.Warning.ToMarkup();

    public async Task OnEventAsync(StartInitializationEvent initializationEvent, CancellationToken cancellationToken)
    {
        EnsureLive(cancellationToken);

        lock (_stateLock)
        {
            Apply(initializationEvent);
        }

        SignalRedraw();

        if (initializationEvent is StartInitializationCompletedEvent)
        {
            await StopLiveAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopLiveAsync();
    }

    private void Apply(StartInitializationEvent initializationEvent)
    {
        switch (initializationEvent)
        {
            case StartInitializationStepStartedEvent step:
                _steps.Add(new StepState(step.Step));
                break;
            case StartInitializationProgressEvent progress:
                if (FindStep(progress.StepKind) is { } progressStep)
                {
                    progressStep.Percent = Math.Clamp(progress.Percent, 0, 100);
                    progressStep.Message = progress.Message;
                }
                break;
            case StartInitializationStepCompletedEvent completed:
                if (FindStep(completed.StepKind) is { } completedStep)
                {
                    completedStep.Completed = true;
                    completedStep.Percent = 100;
                    completedStep.Message = completed.Message;
                }
                break;
            case StartInitializationCompletedEvent completed:
                _runInfo = completed.Result.RunInfo;
                _hostVersion = completed.Result.HostVersion;
                break;
        }
    }

    private StepState? FindStep(StartInitializationStepKind kind)
        => _steps.LastOrDefault(step => step.Step.Kind == kind);

    private void EnsureLive(CancellationToken cancellationToken)
    {
        if (_liveTask is not null)
        {
            return;
        }

        _redrawSignal = new SemaphoreSlim(initialCount: 1, maxCount: int.MaxValue);
        _liveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _console.Cursor.Hide();
        _liveTask = Task.Run(() => RunLiveLoopAsync(_liveCts.Token), CancellationToken.None);
    }

    private async Task StopLiveAsync()
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
            }
        }

        _console.Cursor.Show();
        _redrawSignal?.Dispose();
        _liveCts?.Dispose();
        _redrawSignal = null;
        _liveCts = null;
        _liveTask = null;
    }

    private async Task RunLiveLoopAsync(CancellationToken cancellationToken)
    {
        await _console.Live(BuildLayout())
            .AutoClear(true)
            .Overflow(VerticalOverflow.Ellipsis)
            .Cropping(VerticalOverflowCropping.Top)
            .StartAsync(async ctx =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await _redrawSignal!.WaitAsync(TimeSpan.FromMilliseconds(250), cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    ctx.UpdateTarget(BuildLayout());
                    ctx.Refresh();
                }
            });
    }

    private void SignalRedraw()
    {
        try
        {
            _redrawSignal?.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private IRenderable BuildLayout()
    {
        StepState[] steps;
        DashboardRunInfo runInfo;
        string? hostVersion;
        lock (_stateLock)
        {
            steps = [.. _steps];
            runInfo = _runInfo;
            hostVersion = _hostVersion;
        }

        IRenderable header = new CompactHeaderBuilder(Theme, runInfo).BuildBanner(hostVersion, listenUri: null);

        return new Rows(
            header,
            new Panel(BuildSteps(steps))
                .Header("Initializing")
                .Border(BoxBorder.Rounded)
                .BorderStyle(Theme.Muted)
                .Expand());
    }

    private IRenderable BuildSteps(StepState[] steps)
    {
        if (steps.Length == 0)
        {
            return new Markup($"[{MutedTag}]Preparing start command initialization...[/]");
        }

        Table table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .Expand()
            .AddColumn(new TableColumn(string.Empty).PadRight(2))
            .AddColumn(new TableColumn(string.Empty).PadRight(0));

        foreach (StepState state in steps)
        {
            table.AddRow(
                new Markup($"[{EmphasisTag}]{Markup.Escape(state.Step.Title)}[/]"),
                new Markup(FormatStatus(state)));
        }

        return table;
    }

    private string FormatStatus(StepState state)
    {
        if (state.Completed)
        {
            return $"[{SuccessTag}]{Markup.Escape(state.Message ?? "complete")}[/]";
        }

        if (state.Step.DisplayKind == StartInitializationDisplayKind.Progress)
        {
            return FormatProgress(state.Percent, state.Message);
        }

        return string.IsNullOrWhiteSpace(state.Message)
            ? $"[{WarningTag}]working...[/]"
            : $"[{WarningTag}]{Markup.Escape(state.Message)}[/]";
    }

    private string FormatProgress(double percent, string? message)
    {
        const int Width = 18;
        int filled = (int)Math.Round(Math.Clamp(percent, 0, 100) / 100 * Width);
        string bar = new string('#', filled) + new string('-', Width - filled);
        string suffix = string.IsNullOrWhiteSpace(message) ? string.Empty : $" {Markup.Escape(message)}";
        return $"[{SuccessTag}][{bar}] {percent,3:0}%[/][{MutedTag}]{suffix}[/]";
    }

    private sealed class StepState(StartInitializationStep step)
    {
        public StartInitializationStep Step { get; } = step;

        public double Percent { get; set; }

        public string? Message { get; set; }

        public bool Completed { get; set; }
    }
}
