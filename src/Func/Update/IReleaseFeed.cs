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
    /// Returns the latest release based on the requested quality level.
    /// </summary>
    /// <param name="includePrerelease">
    /// <c>false</c> to return the latest stable release;
    /// <c>true</c> to return whichever is higher by SemVer precedence
    /// between the latest stable and preview releases.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the manifest cannot be fetched or contains no version for
    /// the requested quality level.
    /// </exception>
    public Task<Release> GetLatestAsync(bool includePrerelease, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a <see cref="Release"/> for the given <paramref name="version"/>
    /// after verifying the artifact exists on the CDN.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the version is not available on the CDN or the request fails.
    /// </exception>
    public Task<Release> GetVersionAsync(SemVersion version, CancellationToken cancellationToken);
}
