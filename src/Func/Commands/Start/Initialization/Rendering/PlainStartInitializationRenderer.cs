// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Commands.Start.Initialization.Rendering;

/// <summary>
/// Plain-text initialization renderer for non-TTY and CI output.
/// </summary>
internal sealed class PlainStartInitializationRenderer(IInteractionService interaction) : IStartInitializationRenderer
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

    public Task OnEventAsync(StartInitializationEvent initializationEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (initializationEvent)
        {
            case StartInitializationStartedEvent started:
                _interaction.WriteLine($"Initializing Azure Functions host (profile: {started.ProfileName})");
                break;
            case StartInitializationStepStartedEvent step:
                _interaction.WriteLine($"[init] {step.Step.Title}");
                break;
            case StartInitializationProgressEvent progress:
                _interaction.WriteLine($"[init] {FormatStepKind(progress.StepKind)} {progress.Percent:0}% {progress.Message}".TrimEnd());
                break;
            case StartInitializationStepCompletedEvent completed:
                _interaction.WriteLine($"[init] {FormatStepKind(completed.StepKind)} complete{FormatMessage(completed.Message)}");
                break;
            case StartInitializationCompletedEvent completed:
                _interaction.WriteLine($"Initialization complete. Host {completed.Result.HostVersion} selected.");
                break;
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static string FormatStepKind(StartInitializationStepKind kind)
        => kind.ToString();

    private static string FormatMessage(string? message)
        => string.IsNullOrWhiteSpace(message) ? string.Empty : $": {message}";
}
