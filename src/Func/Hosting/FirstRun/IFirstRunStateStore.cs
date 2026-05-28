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
    /// Returns true when no first-run marker exists yet, meaning the
    /// user has not yet been offered the setup prompt.
    /// </summary>
    public bool IsFirstRun();

    /// <summary>
    /// Records that the first-run prompt has been handled. Subsequent
    /// calls to <see cref="IsFirstRun"/> will return false.
    /// </summary>
    public Task MarkCompleteAsync(CancellationToken cancellationToken);
}
