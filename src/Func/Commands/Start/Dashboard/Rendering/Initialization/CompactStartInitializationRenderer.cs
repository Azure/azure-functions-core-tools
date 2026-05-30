// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Commands.Start.Initialization.Rendering;

/// <summary>
/// Compact initialization renderer shown before the live dashboard starts.
/// </summary>
internal sealed class CompactStartInitializationRenderer(
    IInteractionService interaction,
    string cliVersion,
    IAnsiConsole? console = null) : IStartInitializationRenderer
{
    private static readonly TimeSpan _refreshInterval = TimeSpan.FromMilliseconds(100);

    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly string _cliVersion = string.IsNullOrWhiteSpace(cliVersion) ? throw new ArgumentException("CLI version cannot be empty.", nameof(cliVersion)) : cliVersion;
    private readonly IAnsiConsole _console = console ?? AnsiConsole.Console;
    private readonly Lock _stateLock = new();
    private readonly SemaphoreSlim _redrawSignal = new(initialCount: 0, maxCount: int.MaxValue);
    private readonly List<StepState> _steps = [];
    private StepState? _activeStep;
    private CancellationTokenSource? _liveCts;
    private Task? _liveTask;
    private int _spinnerFrameIndex;
    private bool _disposed;

    private ITheme Theme => _interaction.Theme;

    public Task OnEventAsync(StartInitializationEvent initializationEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Task? liveTaskToStop = null;
        CancellationTokenSource? liveCtsToStop = null;
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            switch (initializationEvent)
            {
                case StartInitializationStartedEvent:
                    EnsureLiveDisplayStarted();
                    break;
                case StartInitializationStepStartedEvent started:
                    StartStep(started.Step);
                    break;
                case StartInitializationProgressEvent progress:
                    UpdateProgress(progress);
                    break;
                case StartInitializationLogEvent log:
                    WriteLog(log);
                    break;
                case StartInitializationStepCompletedEvent completed:
                    CompleteStep(completed);
                    break;
                case StartInitializationStepFailedEvent failed:
                    FailStep(failed);
                    break;
                case StartInitializationCompletedEvent:
                    liveTaskToStop = _liveTask;
                    liveCtsToStop = _liveCts;
                    _liveTask = null;
                    _liveCts = null;
                    break;
            }
        }

        SignalRedraw();

        return liveTaskToStop is null
            ? Task.CompletedTask
            : StopLiveDisplayAsync(liveTaskToStop, liveCtsToStop!, cancellationToken);
    }

    public async Task<bool> ConfirmAsync(string prompt, bool defaultValue, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Task? liveTaskToStop;
        CancellationTokenSource? liveCtsToStop;
        bool restartLiveDisplay;
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            liveTaskToStop = _liveTask;
            liveCtsToStop = _liveCts;
            restartLiveDisplay = liveTaskToStop is not null;
            _liveTask = null;
            _liveCts = null;
        }

        if (liveTaskToStop is not null)
        {
            await StopLiveDisplayAsync(liveTaskToStop, liveCtsToStop!, cancellationToken);
            WritePromptContext();
        }

        try
        {
            return await _interaction.ConfirmAsync(prompt, defaultValue, cancellationToken);
        }
        finally
        {
            if (restartLiveDisplay && !cancellationToken.IsCancellationRequested)
            {
                lock (_stateLock)
                {
                    if (!_disposed)
                    {
                        EnsureLiveDisplayStarted();
                    }
                }

                SignalRedraw();
            }
        }
    }

    private void WritePromptContext()
    {
        _console.Write(BuildLayout());
        _console.WriteLine();
    }

    private void EnsureLiveDisplayStarted()
    {
        if (_liveTask is not null)
        {
            return;
        }

        _liveCts = new CancellationTokenSource();
        CancellationToken liveToken = _liveCts.Token;
        _liveTask = Task.Run(() => RunLiveDisplayAsync(liveToken));
    }

    private void StartStep(StartInitializationStep step)
    {
        EnsureLiveDisplayStarted();
        var state = new StepState(step);
        _steps.Add(state);
        _activeStep = state;
    }

    private void UpdateProgress(StartInitializationProgressEvent progress)
    {
        if (FindStep(progress.StepId) is not { } step)
        {
            return;
        }

        if (!double.IsNaN(progress.Percent))
        {
            step.Percent = Math.Clamp(progress.Percent, 0, 100);
            step.HasProgress = true;
        }

        if (!string.IsNullOrWhiteSpace(progress.Message))
        {
            step.Message = progress.Message;
        }
    }

    private void WriteLog(StartInitializationLogEvent log)
    {
        if (FindStep(log.StepId) is not { } step)
        {
            return;
        }

        step.TotalLogLineCount++;
        step.LogLines.Enqueue(log.Line);
        while (step.LogLines.Count > StepState.LogTailCapacity)
        {
            step.LogLines.Dequeue();
        }
    }

    private void CompleteStep(StartInitializationStepCompletedEvent completed)
    {
        if (FindStep(completed.StepId) is not { } step)
        {
            return;
        }

        step.Completed = true;
        step.Percent = 100;
        step.HasProgress = true;
        step.Result = completed.Message;
        if (ReferenceEquals(_activeStep, step))
        {
            _activeStep = null;
        }
    }

    private void FailStep(StartInitializationStepFailedEvent failed)
    {
        if (FindStep(failed.StepId) is not { } step)
        {
            return;
        }

        step.Failed = true;
        step.Result = failed.Message;
        if (ReferenceEquals(_activeStep, step))
        {
            _activeStep = null;
        }
    }

    private StepState? FindStep(string id)
        => _steps.LastOrDefault(step => string.Equals(step.Step.Id, id, StringComparison.Ordinal));

    private async Task RunLiveDisplayAsync(CancellationToken cancellationToken)
    {
        await _console.Live(BuildLayout())
            .AutoClear(true)
            .Overflow(VerticalOverflow.Ellipsis)
            .Cropping(VerticalOverflowCropping.Top)
            .StartAsync(async ctx =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    ctx.UpdateTarget(BuildLayout(advanceSpinner: true));

                    try
                    {
                        await _redrawSignal.WaitAsync(_refreshInterval, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            });
    }

    private async Task StopLiveDisplayAsync(Task liveTask, CancellationTokenSource liveCts, CancellationToken cancellationToken)
    {
        await liveCts.CancelAsync();
        SignalRedraw();

        try
        {
            await liveTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (liveCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Expected when the live loop observes our shutdown token.
        }
        finally
        {
            liveCts.Dispose();
        }
    }

    private IRenderable BuildLayout(bool advanceSpinner = false)
    {
        StepSnapshot[] steps;
        int spinnerFrameIndex;
        lock (_stateLock)
        {
            if (advanceSpinner)
            {
                _spinnerFrameIndex++;
            }

            spinnerFrameIndex = _spinnerFrameIndex;
            steps =
            [
                .. _steps.Select(static step => new StepSnapshot(
                    step.Step,
                    step.Percent,
                    step.HasProgress,
                    step.Completed,
                    step.Failed,
                    step.Message,
                    step.Result,
                    step.TotalLogLineCount,
                    [.. step.LogLines]))
            ];
        }

        List<IRenderable> rows =
        [
            new Markup(Markup.Escape("Azure Functions CLI")),
            new Markup(Markup.Escape(_cliVersion)),
            new Text(string.Empty),
        ];

        foreach (StepSnapshot step in steps)
        {
            rows.Add(new Markup(BuildStepMarkup(step, spinnerFrameIndex)));
            foreach (string logRow in BuildLogRows(step))
            {
                rows.Add(new Markup(logRow));
            }
        }

        return new Rows(rows);
    }

    private string BuildStepMarkup(StepSnapshot step, int spinnerFrameIndex)
    {
        string icon = step.Completed
            ? Styled(CompletedIcon, Theme.Success)
            : step.Failed
            ? Styled(FailedIcon, Theme.Error)
            : Styled(GetSpinnerFrame(spinnerFrameIndex), Theme.Active);

        string title = Markup.Escape(FormatStepTitle(step));

        string progress = step.Step.DisplayKind == StartInitializationDisplayKind.Progress && !step.Completed && !step.Failed && step.HasProgress
            ? $" {FormatProgress(step.Percent)}"
            : string.Empty;

        if (step.Completed || step.Failed)
        {
            string result = step.Result is null
                ? string.Empty
                : $": [dim]{Markup.Escape(step.Result)}[/]";

            return $"{icon} {title}{result}";
        }
        else
        {
            string message = string.IsNullOrWhiteSpace(step.Message)
                ? string.Empty
                : $" [dim]({Markup.Escape(step.Message)})[/]";

            return $"{icon} [dim]{title}[/]{progress}{message}";
        }
    }

    private IEnumerable<string> BuildLogRows(StepSnapshot step)
    {
        if (step.TotalLogLineCount == 0)
        {
            yield break;
        }

        if (step.Completed && !step.Failed)
        {
            yield return $"  [dim]{LogSummaryPrefix} {step.TotalLogLineCount:0} lines\u2026[/]";
            yield break;
        }

        foreach (string line in step.LogLines)
        {
            yield return $"  [dim]{LogGutter} {Markup.Escape(line)}[/]";
        }
    }

    private string FormatProgress(double percent)
    {
        const int width = 18;
        double clamped = Math.Clamp(percent, 0, 100);
        int completed = (int)Math.Round(width * clamped / 100, MidpointRounding.AwayFromZero);
        string completedText = new(ProgressCompleteCharacter, completed);
        string remainingText = new(ProgressRemainingCharacter, width - completed);

        return $"{Styled(completedText, Theme.Success)}{Styled(remainingText, Theme.Muted)} {clamped,3:0}%";
    }

    private string GetSpinnerFrame(int frameIndex)
    {
        Spinner spinner = _console.Profile.Capabilities.Unicode ? Spinner.Known.Dots : Spinner.Known.Line;
        return spinner.Frames[frameIndex % spinner.Frames.Count];
    }

    private string CompletedIcon => _console.Profile.Capabilities.Unicode ? "\u2713" : "[x]";

    private string FailedIcon => _console.Profile.Capabilities.Unicode ? "\u00d7" : "[!]";

    private string LogGutter => _console.Profile.Capabilities.Unicode ? "\u2502" : "|";

    private string LogSummaryPrefix => _console.Profile.Capabilities.Unicode ? "\u2514" : "`";

    private char ProgressCompleteCharacter => _console.Profile.Capabilities.Unicode ? '\u2501' : '=';

    private char ProgressRemainingCharacter => _console.Profile.Capabilities.Unicode ? '\u2500' : '-';

    private static string Styled(string text, Style style)
        => string.IsNullOrEmpty(text)
            ? string.Empty
            : $"[{style.ToMarkup()}]{Markup.Escape(text)}[/]";

    private static string FormatStepTitle(StepSnapshot step)
        => step.Completed || step.Step.DisplayKind == StartInitializationDisplayKind.Progress
        ? step.Step.Title
        : step.Step.Title.EndsWith("...", StringComparison.Ordinal) ? step.Step.Title : $"{step.Step.Title}...";

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        Task? liveTaskToStop;
        CancellationTokenSource? liveCtsToStop;
        lock (_stateLock)
        {
            _disposed = true;
            liveTaskToStop = _liveTask;
            liveCtsToStop = _liveCts;
            _liveTask = null;
            _liveCts = null;
        }

        if (liveTaskToStop is not null)
        {
            await StopLiveDisplayAsync(liveTaskToStop, liveCtsToStop!, CancellationToken.None);
        }

        _redrawSignal.Dispose();
    }

    private void SignalRedraw()
    {
        try
        {
            _redrawSignal.Release();
        }
        catch (ObjectDisposedException)
        {
            // The live display has already been torn down.
        }
        catch (SemaphoreFullException)
        {
            // The live loop coalesces pending redraw requests.
        }
    }

    private sealed class StepState(StartInitializationStep step)
    {
        public const int LogTailCapacity = 10;

        public StartInitializationStep Step { get; } = step;

        public double Percent { get; set; }

        public bool HasProgress { get; set; }

        public bool Completed { get; set; }

        public bool Failed { get; set; }

        public string? Message { get; set; }

        public string? Result { get; internal set; }

        public Queue<string> LogLines { get; } = [];

        public int TotalLogLineCount { get; set; }
    }

    private sealed record StepSnapshot(
        StartInitializationStep Step,
        double Percent,
        bool HasProgress,
        bool Completed,
        bool Failed,
        string? Message,
        string? Result,
        int TotalLogLineCount,
        IReadOnlyList<string> LogLines);
}
