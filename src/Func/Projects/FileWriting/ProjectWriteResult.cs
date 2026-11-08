// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;

namespace Azure.Functions.Cli.Projects.FileWriting;

internal enum ProjectWriteStatus
{
    Created,
    Preserved,
    Failed,
    NotAttempted,
    RemovedAfterFailure,
    CleanupPending,
    Uncertain,
}

/// <summary>
/// Reports one operation. The destination directory uses its absolute path; descendant paths are relative to that destination.
/// </summary>
/// <remarks>
/// An unexpected exception after entry creation carries this effect in <see cref="Exception.Data"/>, keyed by <c>typeof(ProjectWriteEffect)</c>.
/// </remarks>
internal sealed record ProjectWriteEffect(
    string Path,
    bool IsDirectory,
    ProjectWriteStatus Status,
    Exception? Error = null,
    Exception? CleanupError = null,
    Exception? CompletionError = null,
    bool CleanupRequested = false);

/// <summary>
/// Reports actual effects. Unexpected exceptions from the writer after entry creation carry this result in
/// <see cref="Exception.Data"/>, keyed by <c>typeof(ProjectWriteResult)</c>.
/// </summary>
internal sealed record ProjectWriteResult(ImmutableArray<ProjectWriteEffect> Effects, OperationCanceledException? Cancellation = null)
{
    public bool Canceled => Cancellation is not null;

    public bool Succeeded => !Canceled && Effects.All(effect => effect.Status is ProjectWriteStatus.Created or ProjectWriteStatus.Preserved);
}
