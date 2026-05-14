// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Threading.Channels;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Spectre.Console;

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
    private readonly SemaphoreSlim _stepLock = new(initialCount: 1, maxCount: 1);
    private DashboardRunInfo _runInfo = runInfo ?? new();
    private string? _hostVersion;
    private StepDisplay? _currentDisplay;
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
                    RenderHeader();
                    break;
                case StartInitializationStepStartedEvent started:
                    await StartDisplayAsync(started.Step, cancellationToken);
                    break;
                case StartInitializationProgressEvent progress:
                    await SendToCurrentDisplayAsync(progress, cancellationToken);
                    break;
                case StartInitializationStepCompletedEvent completed:
                    ApplyStepCompletion(completed);
                    await CompleteCurrentDisplayAsync(completed, cancellationToken);
                    break;
                case StartInitializationCompletedEvent completed:
                    _runInfo = completed.Result.RunInfo;
                    _hostVersion = completed.Result.HostVersion;
                    await StopCurrentDisplayAsync(cancellationToken);
                    ClearIfTerminal();
                    break;
            }
        }
        finally
        {
            _stepLock.Release();
        }
    }

    private async Task StartDisplayAsync(StartInitializationStep step, CancellationToken cancellationToken)
    {
        await StopCurrentDisplayAsync(cancellationToken);
        RenderHeader();

        var display = new StepDisplay(step);
        display.DisplayTask = Task.Run(
            () => step.DisplayKind == StartInitializationDisplayKind.Progress
                ? RunProgressDisplayAsync(display)
                : RunStatusDisplayAsync(display),
            CancellationToken.None);

        _ = display.DisplayTask.ContinueWith(
            task =>
            {
                if (task.Exception is { } exception)
                {
                    display.Ready.TrySetException(exception.GetBaseException());
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

        _currentDisplay = display;
        await display.Ready.Task.WaitAsync(cancellationToken);
    }

    private async Task RunStatusDisplayAsync(StepDisplay display)
    {
        await _console.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Theme.Active)
            .StartAsync(display.Step.Title, async status =>
            {
                display.Ready.TrySetResult();

                await foreach (StartInitializationEvent initializationEvent in display.Events.Reader.ReadAllAsync(display.CancellationToken))
                {
                    if (initializationEvent is StartInitializationProgressEvent progress
                        && !string.IsNullOrWhiteSpace(progress.Message))
                    {
                        status.Status = progress.Message;
                        status.Refresh();
                    }

                    if (initializationEvent is StartInitializationStepCompletedEvent completed)
                    {
                        status.Status = completed.Message ?? display.Step.Title;
                        status.Refresh();
                        return;
                    }
                }
            });
    }

    private async Task RunProgressDisplayAsync(StepDisplay display)
    {
        await _console.Progress()
            .AutoClear(true)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn
                {
                    CompletedStyle = Theme.Success,
                    FinishedStyle = Theme.Success,
                    RemainingStyle = Theme.Muted,
                },
                new PercentageColumn())
            .StartAsync(async context =>
            {
                ProgressTask task = context.AddTask(display.Step.Title, maxValue: 100);
                display.Ready.TrySetResult();

                await foreach (StartInitializationEvent initializationEvent in display.Events.Reader.ReadAllAsync(display.CancellationToken))
                {
                    if (initializationEvent is StartInitializationProgressEvent progress)
                    {
                        task.Value = Math.Clamp(progress.Percent, 0, 100);
                        if (!string.IsNullOrWhiteSpace(progress.Message))
                        {
                            task.Description = progress.Message;
                        }
                    }

                    if (initializationEvent is StartInitializationStepCompletedEvent completed)
                    {
                        task.Value = 100;
                        task.Description = completed.Message ?? display.Step.Title;
                        task.StopTask();
                        return;
                    }
                }
            });
    }

    private async Task SendToCurrentDisplayAsync(StartInitializationEvent initializationEvent, CancellationToken cancellationToken)
    {
        if (_currentDisplay is null)
        {
            return;
        }

        await _currentDisplay.Events.Writer.WriteAsync(initializationEvent, cancellationToken);
    }

    private async Task CompleteCurrentDisplayAsync(StartInitializationStepCompletedEvent completed, CancellationToken cancellationToken)
    {
        if (_currentDisplay is not { } display || display.Step.Kind != completed.StepKind)
        {
            return;
        }

        await display.Events.Writer.WriteAsync(completed, cancellationToken);
        display.Events.Writer.TryComplete();
        await AwaitDisplayAsync(display, cancellationToken);
        _currentDisplay = null;
    }

    private async Task StopCurrentDisplayAsync(CancellationToken cancellationToken)
    {
        if (_currentDisplay is not { } display)
        {
            return;
        }

        display.Cancel();
        display.Events.Writer.TryComplete();
        await AwaitDisplayAsync(display, cancellationToken);
        _currentDisplay = null;
    }

    private static async Task AwaitDisplayAsync(StepDisplay display, CancellationToken cancellationToken)
    {
        try
        {
            await display.DisplayTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (display.CancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            display.Dispose();
        }
    }

    private void ApplyStepCompletion(StartInitializationStepCompletedEvent completed)
    {
        if (completed.StepKind == StartInitializationStepKind.ResolveStack
            && !string.IsNullOrWhiteSpace(completed.Message))
        {
            _runInfo = _runInfo with { StackName = completed.Message };
        }
    }

    private void RenderHeader()
    {
        ClearIfTerminal();
        _console.Write(new CompactHeaderBuilder(Theme, _runInfo).BuildBanner(_hostVersion, listenUri: null));
    }

    private void ClearIfTerminal()
    {
        if (_console.Profile.Out.IsTerminal)
        {
            _console.Clear(home: true);
        }
    }

    private sealed class StepDisplay(StartInitializationStep step) : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();

        public StartInitializationStep Step { get; } = step;

        public Channel<StartInitializationEvent> Events { get; } = Channel.CreateUnbounded<StartInitializationEvent>();

        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DisplayTask { get; set; } = Task.CompletedTask;

        public CancellationToken CancellationToken => _cts.Token;

        public void Cancel() => _cts.Cancel();

        public void Dispose() => _cts.Dispose();
    }

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
            await StopCurrentDisplayAsync(CancellationToken.None);
        }
        finally
        {
            _stepLock.Release();
        }

        _stepLock.Dispose();
    }
}
