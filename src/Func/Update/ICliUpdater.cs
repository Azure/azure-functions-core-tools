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
    /// Preloads process-launch dependencies, downloads and checks the release artifact,
    /// then installs it with backup/rollback and verifies the installed executable with <c>--version</c>.
    /// </summary>
    /// <param name="release">The release to install.</param>
    /// <param name="progress">
    /// Optional receiver for pipeline progress updates (download byte counts
    /// plus phase transitions). Pass <see langword="null"/> when no UI is
    /// attached; the updater does not require a progress sink to function.
    /// </param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    public Task UpdateAsync(Release release, IProgress<UpdateProgress>? progress, CancellationToken cancellationToken);
}
