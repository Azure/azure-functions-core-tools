// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Represents one startup initialization operation.
/// </summary>
internal interface IStartInitializationStep
{
    public string Id { get; }

    public string Title { get; }

    public string? Detail { get; }

    public StartInitializationDisplayKind DisplayKind { get; }

    public Task<StartInitializationStepResult> ExecuteAsync(
        StartInitializationStepContext context,
        CancellationToken cancellationToken);
}
