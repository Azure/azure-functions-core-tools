// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Metadata for a startup initialization step.
/// </summary>
internal sealed record StartInitializationStep(
    string Id,
    string Title,
    string? Detail = null,
    StartInitializationDisplayKind DisplayKind = StartInitializationDisplayKind.Status)
{
    public StartInitializationStep(IStartInitializationStep step)
        : this(
            step?.Id ?? throw new ArgumentNullException(nameof(step)),
            step.Title,
            step.Detail,
            step.DisplayKind)
    {
    }

    public string Id { get; init; } = string.IsNullOrWhiteSpace(Id)
        ? throw new ArgumentException("Step id cannot be empty.", nameof(Id))
        : Id;

    public string Title { get; init; } = string.IsNullOrWhiteSpace(Title)
        ? throw new ArgumentException("Step title cannot be empty.", nameof(Title))
        : Title;
}
