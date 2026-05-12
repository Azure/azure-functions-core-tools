// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Per-source surface the catalog talks to, uniform across v3 NuGet feeds and local folders.
/// Kept as an interface to give the aggregator a clean substitution seam in tests; production
/// has exactly two implementations.
/// </summary>
internal interface ISourceClient
{
    public PackageSource Source { get; }

    public Task<IReadOnlyList<CatalogSearchResult>> SearchAsync(
        string? query,
        bool includePrerelease,
        int skip,
        int take,
        CancellationToken cancellationToken);

    public Task<IReadOnlyList<NuGetVersion>> ListVersionsAsync(
        string packageId,
        CancellationToken cancellationToken);

    public Task<Stream> OpenPackageAsync(
        string packageId,
        NuGetVersion version,
        CancellationToken cancellationToken);
}
