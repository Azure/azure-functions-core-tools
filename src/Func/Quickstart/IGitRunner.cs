// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Wraps the local <c>git</c> executable for quickstart fetching. Implementations
/// must use argument-array invocation (no shell concatenation) and run git in
/// a non-interactive environment so no credential prompt can ever block.
/// </summary>
internal interface IGitRunner
{
    /// <summary>
    /// Returns true when <c>git</c> is on PATH and at version 2.25 or higher
    /// (the minimum that supports <c>--sparse</c> for cone-mode sparse checkout).
    /// </summary>
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Shallow-clones <paramref name="repositoryUrl"/> at <paramref name="gitRef"/>
    /// into <paramref name="targetDirectory"/>. When <paramref name="folderPath"/>
    /// is non-empty and not <c>"."</c>, only that subfolder's blobs are fetched
    /// via cone-mode sparse-checkout. <paramref name="gitRef"/> may name a branch
    /// or tag; pass <see langword="null"/> or <c>"HEAD"</c> to clone the remote's
    /// default branch.
    /// </summary>
    public Task<GitCloneResult> ShallowCloneAsync(
        string repositoryUrl,
        string? gitRef,
        string targetDirectory,
        string? folderPath,
        CancellationToken cancellationToken);
}

/// <summary>
/// Outcome of a git invocation. <see cref="ExitCode"/> of zero indicates success.
/// </summary>
internal readonly record struct GitCloneResult(int ExitCode, string Stderr);
