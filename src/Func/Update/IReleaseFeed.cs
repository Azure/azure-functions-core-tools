// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Semver;

namespace Azure.Functions.Cli.Update;

/// <summary>
/// Abstraction over the source of func CLI releases. The production
/// implementation queries a version manifest on the Azure Functions CDN;
/// tests substitute it with an in-memory feed.
/// </summary>
internal interface IReleaseFeed
{
    /// <summary>
    /// Returns the latest release on the requested channel, or <c>null</c>
    /// when no version is published for that channel.
    /// </summary>
    /// <param name="includePrerelease">
    /// <c>false</c> to return the latest stable release;
    /// <c>true</c> to return the latest preview release.
    /// </param>
    public Task<Release?> GetLatestAsync(bool includePrerelease, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a <see cref="Release"/> for the given <paramref name="version"/>
    /// if it exists on the CDN (i.e. the zip artifact is downloadable), or
    /// <c>null</c> when the version is not available.
    /// Used by <c>func update --version X.Y.Z</c>.
    /// </summary>
    public Task<Release?> GetVersionAsync(SemVersion version, CancellationToken cancellationToken);
}
