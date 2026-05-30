// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Base type for prototype initialization steps that simulate host startup work.
/// </summary>
internal abstract class FuncStartInitializationStep : IStartInitializationStep
{
    public abstract string Id { get; }

    public abstract string Title { get; }

    public virtual string? Detail => null;

    public virtual StartInitializationDisplayKind DisplayKind => StartInitializationDisplayKind.Status;

    public abstract Task<StartInitializationStepResult> ExecuteAsync(StartInitializationStepContext context, CancellationToken cancellationToken);
}
