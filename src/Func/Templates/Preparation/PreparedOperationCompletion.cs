// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Preparation;

internal enum PreparedOperationState
{
    Prepared,
    Committing,
    Completed,
    Disposing,
    Disposed,
}

internal enum PreparedCommitStatus
{
    NotStarted,
    InProgress,
    Succeeded,
    Canceled,
    Failed,
}

internal enum PreparedCommitStage
{
    NotStarted,
    Preflight,
    Publication,
    Complete,
}

internal enum PreparedCleanupStatus
{
    Pending,
    InProgress,
    Succeeded,
    Failed,
}

internal sealed record PreparedEffect(string Path, PreparedOutputKind Kind, bool IsConfiguration = false);

/// <summary>
/// Records actual completed effects independently of the immutable plan, including unfinished publication and cleanup.
/// </summary>
internal sealed class PreparedOperationCompletion(
    PreparedCommitStatus status,
    PreparedCommitStage stage,
    IEnumerable<PreparedEffect> completedEffects,
    PreparedEffect? pendingEffect,
    Exception? commitError,
    PreparedCleanupStatus cleanupStatus,
    IEnumerable<Exception> cleanupErrors)
{
    public PreparedCommitStatus Status { get; } = status;

    public PreparedCommitStage Stage { get; } = stage;

    public IReadOnlyList<PreparedEffect> CompletedEffects { get; } =
        Array.AsReadOnly<PreparedEffect>([.. completedEffects ?? throw new ArgumentNullException(nameof(completedEffects))]);

    public PreparedEffect? PendingEffect { get; } = pendingEffect;

    public Exception? CommitError { get; } = commitError;

    public bool ConfigurationCompleted => CompletedEffects.Any(effect => effect.IsConfiguration);

    public PreparedCleanupStatus CleanupStatus { get; } = cleanupStatus;

    public IReadOnlyList<Exception> CleanupErrors { get; } =
        Array.AsReadOnly<Exception>([.. cleanupErrors ?? throw new ArgumentNullException(nameof(cleanupErrors))]);
}
