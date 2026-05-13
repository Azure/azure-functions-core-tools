// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Reads from the configured package source to surface workloads. All
/// operations filter by the <c>FuncCliWorkload</c> package type so arbitrary
/// NuGet packages never leak into workload commands.
/// </summary>
/// <remarks>
/// Backed by NuGet by design. The abstraction exists so command code stays
/// workload-flavoured (and unit-testable), not to enable swapping the
/// distribution mechanism. Treat NuGet as a permanent dependency.
/// </remarks>
internal interface IWorkloadCatalog
{
    /// <summary>
    /// Searches the configured source for workload packages.
    /// </summary>
    /// <param name="query">Filter, paging, and source-override bag.</param>
    /// <param name="cancellationToken">Cancellation propagated to the underlying request.</param>
    public Task<IReadOnlyList<CatalogSearchResult>> SearchAsync(
        CatalogSearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the highest installable version of <paramref name="packageId"/>
    /// from the configured source, or <c>null</c> when no version matches.
    /// </summary>
    /// <param name="packageId">NuGet package id; case-insensitive.</param>
    /// <param name="includePrerelease"><c>true</c> to allow prerelease versions in the result.</param>
    /// <param name="currentVersion">
    /// Optional currently-installed version. When non-null and
    /// <paramref name="allowMajor"/> is <c>false</c>, candidates are
    /// constrained to the same major version.
    /// </param>
    /// <param name="allowMajor">
    /// When <c>true</c>, candidates may cross a major-version boundary.
    /// Default <c>true</c> matches install behaviour; update sets it
    /// <c>false</c> unless <c>--major</c> was passed.
    /// </param>
    /// <param name="overrideSource">Optional <c>--source</c> override.</param>
    /// <param name="cancellationToken">Cancellation propagated to the underlying request.</param>
    public Task<ResolvedPackage?> ResolveLatestVersionAsync(
        string packageId,
        bool includePrerelease,
        NuGetVersion? currentVersion = null,
        bool allowMajor = true,
        string? overrideSource = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves an exact <paramref name="version"/> of <paramref name="packageId"/>
    /// against the configured source. Returns <c>null</c> when the source does
    /// not advertise that version. Used by <c>install --version</c>.
    /// </summary>
    /// <param name="packageId">NuGet package id; case-insensitive.</param>
    /// <param name="version">Exact version to locate.</param>
    /// <param name="overrideSource">Optional <c>--source</c> override.</param>
    /// <param name="cancellationToken">Cancellation propagated to the underlying request.</param>
    public Task<ResolvedPackage?> ResolveVersionAsync(
        string packageId,
        NuGetVersion version,
        string? overrideSource = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the <c>.nupkg</c> for <paramref name="package"/> to a temp
    /// file and returns an open read stream over it. The caller owns the
    /// returned stream; closing it removes the temp file.
    /// </summary>
    /// <param name="package">Package + source returned from one of the resolve methods.</param>
    /// <param name="cancellationToken">Cancellation propagated to the underlying HTTP / file read.</param>
    /// <exception cref="WorkloadPackageNotFoundException">
    /// The package + version does not exist on the resolved source.
    /// </exception>
    public Task<Stream> DownloadAsync(
        ResolvedPackage package,
        CancellationToken cancellationToken = default);
}
