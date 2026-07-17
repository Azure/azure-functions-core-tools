// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

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
                _interaction.WriteLine(FormatProgress(progress));
                break;
            case StartInitializationLogEvent log:
                _interaction.WriteLine($"[init] {log.StepId}  {log.Line}");
                break;
            case StartInitializationStepCompletedEvent completed:
                _interaction.WriteLine($"[init] {completed.StepId} complete{FormatMessage(completed.Message)}");
                break;
            case StartInitializationStepFailedEvent failed:
                _interaction.WriteLine($"[init] {failed.StepId} failed{FormatMessage(failed.Message)}");
                break;
            case StartInitializationCompletedEvent completed:
                _interaction.WriteLine(
                    $"Initialization complete. Host {completed.Result.HostVersion} selected.{FormatProfile(completed.Result.Profile)}");
                break;
        }

        return Task.CompletedTask;
    }

    public async Task<bool> ConfirmAsync(string prompt, bool defaultValue, CancellationToken cancellationToken)
        => await _interaction.ConfirmAsync(prompt, defaultValue, cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static string FormatMessage(string? message)
        => string.IsNullOrWhiteSpace(message) ? string.Empty : $": {message}";

    private static string FormatProgress(StartInitializationProgressEvent progress)
        => double.IsNaN(progress.Percent)
            ? $"[init] {progress.StepId} {progress.Message}".TrimEnd()
            : $"[init] {progress.StepId} {progress.Percent:0}% {progress.Message}".TrimEnd();

    private static string FormatProfile(StartInitializationProfileInfo? profile)
        => profile is null
            ? string.Empty
            : $" Profile {profile.Name} from {profile.SourceDisplayName}.";
}
