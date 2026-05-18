// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Spectre.Console;

namespace Azure.Functions.Cli.Commands.Start.Initialization.Rendering;

/// <summary>
/// Compact initialization renderer shown before the live dashboard starts.
/// </summary>
internal sealed class CompactStartInitializationRenderer(
    IInteractionService interaction,
    string cliVersion,
    IAnsiConsole? console = null) : IStartInitializationRenderer
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly string _cliVersion = string.IsNullOrWhiteSpace(cliVersion) ? throw new ArgumentException("CLI version cannot be empty.", nameof(cliVersion)) : cliVersion;
    private readonly IAnsiConsole _console = console ?? AnsiConsole.Console;
    private readonly SemaphoreSlim _stepLock = new(initialCount: 1, maxCount: 1);
    private readonly List<StepState> _steps = [];
    private StepState? _activeStep;
    private bool _preambleRendered;
    private bool _disposed;

    private ITheme Theme => _interaction.Theme;

    public async Task OnEventAsync(StartInitializationEvent initializationEvent, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        await _stepLock.WaitAsync(cancellationToken);
        try
        {
            switch (initializationEvent)
            {
                case StartInitializationStartedEvent:
                    RenderPreamble();
                    break;
                case StartInitializationStepStartedEvent started:
                    StartStep(started.Step);
                    break;
                case StartInitializationProgressEvent progress:
                    UpdateProgress(progress);
                    break;
                case StartInitializationStepCompletedEvent completed:
                    CompleteStep(completed);
                    break;
                case StartInitializationCompletedEvent:
                    ClearInitializationBlockIfTerminal();
                    break;
            }
        }
        finally
        {
            _stepLock.Release();
        }
    }

    private void StartStep(StartInitializationStep step)
    {
        RenderPreamble();
        var state = new StepState(step);
        _steps.Add(state);
        _activeStep = state;
        WriteStepLine(state, endLine: false);
    }

    private void RenderPreamble()
    {
        if (_preambleRendered)
        {
            return;
        }

        TextWriter writer = _console.Profile.Out.Writer;
        writer.WriteLine("Azure Functions CLI");
        writer.WriteLine(_cliVersion);
        writer.WriteLine();
        _preambleRendered = true;
    }

    private void UpdateProgress(StartInitializationProgressEvent progress)
    {
        if (FindStep(progress.StepKind) is not { } step)
        {
            return;
        }

        step.Percent = Math.Clamp(progress.Percent, 0, 100);
        if (ReferenceEquals(_activeStep, step))
        {
            RewriteStepLine(step, endLine: false);
        }
    }

    private void CompleteStep(StartInitializationStepCompletedEvent completed)
    {
        if (FindStep(completed.StepKind) is not { } step)
        {
            return;
        }

        step.Completed = true;
        step.Percent = 100;
        step.Result = completed.Message;
        if (ReferenceEquals(_activeStep, step))
        {
            RewriteStepLine(step, endLine: true);
            _activeStep = null;
        }
    }

    private StepState? FindStep(StartInitializationStepKind kind)
        => _steps.LastOrDefault(step => step.Step.Kind == kind);

    private void WriteStepLine(StepState step, bool endLine)
    {
        _console.Write(new Markup(BuildStepMarkup(step)));
        if (endLine)
        {
            _console.Write(new Text(Environment.NewLine));
        }
    }

    private void RewriteStepLine(StepState step, bool endLine)
    {
        ClearCurrentLine();
        WriteStepLine(step, endLine);
    }

    private void ClearCurrentLine()
    {
        if (_console.Profile.Out.IsTerminal)
        {
            _console.Write(new ControlCode("\r\u001b[2K"));
        }
    }

    private string BuildStepMarkup(StepState step)
    {
        string icon = step.Completed
            ? Styled(CompletedIcon, Theme.Success)
            : Styled(CurrentSpinnerFrame, Theme.Active);

        string title = Markup.Escape(FormatStepTitle(step));

        string progress = step.Step.DisplayKind == StartInitializationDisplayKind.Progress && !step.Completed
            ? $" {FormatProgress(step.Percent)}"
            : string.Empty;

        if (step.Completed)
        {
            string result = step.Result is null
                ? string.Empty
                : $": [dim]{Markup.Escape(step.Result)}[/]";

            return $"{icon} {title}{result}";
        }
        else
        {
            return $"{icon} [dim]{title}[/]{progress}";
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

    private string CurrentSpinnerFrame
    {
        get
        {
            Spinner spinner = _console.Profile.Capabilities.Unicode ? Spinner.Known.Dots : Spinner.Known.Line;
            return spinner.Frames[0];
        }
    }

    private string CompletedIcon => _console.Profile.Capabilities.Unicode ? "\u2713" : "[x]";

    private char ProgressCompleteCharacter => _console.Profile.Capabilities.Unicode ? '\u2501' : '=';

    private char ProgressRemainingCharacter => _console.Profile.Capabilities.Unicode ? '\u2500' : '-';

    private void ClearInitializationBlockIfTerminal()
    {
        if (!_console.Profile.Out.IsTerminal || _steps.Count == 0)
        {
            return;
        }

        const int preambleLineCount = 3;
        int linesToMove = _activeStep is null
            ? _steps.Count
            : Math.Max(_steps.Count - 1, 0);
        linesToMove += _preambleRendered ? preambleLineCount : 0;

        string moveToFirstLine = linesToMove > 0
            ? $"\r\u001b[{linesToMove}A"
            : "\r";

        _console.Write(new ControlCode($"{moveToFirstLine}\u001b[J"));
    }

    private static string Styled(string text, Style style)
        => string.IsNullOrEmpty(text)
            ? string.Empty
            : $"[{style.ToMarkup()}]{Markup.Escape(text)}[/]";

    private static string FormatStepTitle(StepState step)
        => step.Completed || step.Step.DisplayKind == StartInitializationDisplayKind.Progress
        ? step.Step.Title
        : step.Step.Title.EndsWith("...", StringComparison.Ordinal) ? step.Step.Title : $"{step.Step.Title}...";

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _stepLock.WaitAsync();
        try
        {
            _disposed = true;
            if (_activeStep is not null)
            {
                _console.Write(new Text(Environment.NewLine));
                _activeStep = null;
            }
        }
        finally
        {
            _stepLock.Release();
        }

        _stepLock.Dispose();
    }

    private sealed class StepState(StartInitializationStep step)
    {
        public StartInitializationStep Step { get; } = step;

        public double Percent { get; set; }

        public bool Completed { get; set; }
        public string? Result { get; internal set; }
    }
}
