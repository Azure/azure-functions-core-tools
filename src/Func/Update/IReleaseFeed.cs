// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Semver;

namespace Azure.Functions.Cli.Update;

/// <summary>
/// Abstraction over the source of func CLI releases. The production
/// implementation queries the public GitHub releases API; tests substitute it
/// with an in-memory feed.
/// </summary>
internal interface IReleaseFeed
{
    /// <summary>
    /// Returns the SemVer-max release on the requested channel, or
    /// <c>null</c> when the channel has no releases.
    /// </summary>
    /// <param name="includePrerelease">
    /// <c>false</c> to consider only stable releases (no SemVer prerelease label);
    /// <c>true</c> to consider stable and prerelease releases together.
    /// </param>
    public Task<Release?> GetLatestAsync(bool includePrerelease, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the release whose tag parses to the requested
    /// <paramref name="version"/>, or <c>null</c> when no such release exists.
    /// Used by <c>func update --version X.Y.Z</c>.
    /// </summary>
    public Task<Release?> GetVersionAsync(SemVersion version, CancellationToken cancellationToken);
}
