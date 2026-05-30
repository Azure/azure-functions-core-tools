// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Initialization.Rendering;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Context available to a startup initialization step while it executes.
/// </summary>
internal sealed class StartInitializationStepContext(
    StartInitializationContext initialization,
    StartInitializationState state,
    IStartInitializationStep step,
    IStartInitializationRenderer renderer,
    TimeProvider timeProvider)
{
    private readonly IStartInitializationStep _step = step ?? throw new ArgumentNullException(nameof(step));
    private readonly IStartInitializationRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly List<IStartInitializationStep> _nextSteps = [];

    public StartInitializationContext Initialization { get; } = initialization ?? throw new ArgumentNullException(nameof(initialization));

    public StartCommandOptions Options => Initialization.Options;

    public StartInitializationState State { get; } = state ?? throw new ArgumentNullException(nameof(state));

    public bool CanPrompt => Initialization.CanPrompt;

    public void AddNext(IStartInitializationStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _nextSteps.Add(step);
    }

    public async Task ReportProgressAsync(double percent, string? message, CancellationToken cancellationToken)
    {
        var progressEvent = new StartInitializationProgressEvent(_timeProvider.GetUtcNow(), _step.Id, percent, message);

        await _renderer.OnEventAsync(progressEvent, cancellationToken);
    }

    public async Task ReportStatusAsync(string message, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var progressEvent = new StartInitializationProgressEvent(_timeProvider.GetUtcNow(), _step.Id, double.NaN, message);

        await _renderer.OnEventAsync(progressEvent, cancellationToken);
    }

    public async Task ReportLogAsync(string line, FunctionsProjectReportSeverity severity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(line);

        var logEvent = new StartInitializationLogEvent(_timeProvider.GetUtcNow(), _step.Id, line, severity);

        await _renderer.OnEventAsync(logEvent, cancellationToken);
    }

    public async Task<bool> ConfirmAsync(string prompt, bool defaultValue, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        return await _renderer.ConfirmAsync(prompt, defaultValue, cancellationToken);
    }

    internal IReadOnlyList<IStartInitializationStep> DrainNextSteps()
    {
        IStartInitializationStep[] steps = [.. _nextSteps];
        _nextSteps.Clear();
        return steps;
    }
}
