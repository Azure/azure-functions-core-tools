// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Update;

/// <summary>
/// Downloads a func CLI release artifact from the CDN, installs it in place,
/// and rolls back automatically on failure.
/// </summary>
internal interface ICliUpdater
{
    /// <summary>
    /// Downloads the release artifact from CDN, extracts it, swaps the current
    /// binary with backup/rollback, and verifies via spawning <c>func --version</c>.
    /// </summary>
    /// <param name="release">The release to install.</param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    public Task UpdateAsync(Release release, CancellationToken cancellationToken);
}
