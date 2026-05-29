// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting.FirstRun;

/// <summary>
/// Tracks whether the user has been through the first-run experience.
/// Backed by a marker file under the func home directory so the answer
/// survives across invocations.
/// </summary>
internal interface IFirstRunStateStore
{
    /// <summary>
    /// Returns true when no first-run marker exists yet AND the user has no
    /// installed workloads, meaning the user has not yet been offered the
    /// setup prompt and has nothing on disk that would make the prompt
    /// redundant.
    /// </summary>
    public Task<bool> IsFirstRunAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the first-run prompt has been handled. Subsequent
    /// calls to <see cref="IsFirstRunAsync"/> will return false.
    /// </summary>
    public Task MarkCompleteAsync(CancellationToken cancellationToken);
}
