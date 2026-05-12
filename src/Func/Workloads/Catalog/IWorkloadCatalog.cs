// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Reads from one or more configured package sources to surface workloads.
/// All operations filter by the <c>FuncCliWorkload</c> package type so
/// arbitrary NuGet packages never leak into workload commands.
/// </summary>
/// <remarks>
/// Backed by NuGet by design. The abstraction exists so command code stays
/// workload-flavoured (and so the aggregator can be unit-tested), not to
/// enable swapping the distribution mechanism. Treat NuGet as a permanent
/// dependency.
/// </remarks>
internal interface IWorkloadCatalog
{
    /// <summary>
    /// Searches the configured sources for workload packages. Results are deduped by
    /// package id (highest version wins) and ordered highest-version-first.
    /// </summary>
    /// <param name="query">Optional free-form query; null or empty returns all matching packages.</param>
    /// <param name="includePrerelease"><c>true</c> to include prerelease versions.</param>
    /// <param name="skip">Pagination offset applied per source before merge.</param>
    /// <param name="take">Maximum number of results requested per source.</param>
    /// <param name="overrideSource">Optional <c>--source</c> override. When set, only this source is consulted.</param>
    /// <param name="cancellationToken">Cancellation propagated to all per-source requests.</param>
    public Task<IReadOnlyList<CatalogSearchResult>> SearchAsync(
        string? query,
        bool includePrerelease,
        int skip,
        int take,
        string? overrideSource = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the highest installable version of <paramref name="packageId"/>
    /// across the configured sources, or <c>null</c> when no version matches.
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
    /// <param name="cancellationToken">Cancellation propagated to per-source requests.</param>
    public Task<ResolvedPackage?> ResolveLatestVersionAsync(
        string packageId,
        bool includePrerelease,
        NuGetVersion? currentVersion = null,
        bool allowMajor = true,
        string? overrideSource = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the <c>.nupkg</c> for <paramref name="package"/> to a temp
    /// file and returns an open read stream over it. The caller owns the
    /// returned stream; closing it removes the temp file.
    /// </summary>
    /// <param name="package">Package + source returned from <see cref="ResolveLatestVersionAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation propagated to the underlying HTTP / file read.</param>
    /// <exception cref="WorkloadPackageNotFoundException">
    /// The package + version does not exist on the resolved source.
    /// </exception>
    public Task<Stream> DownloadAsync(
        ResolvedPackage package,
        CancellationToken cancellationToken = default);
}
