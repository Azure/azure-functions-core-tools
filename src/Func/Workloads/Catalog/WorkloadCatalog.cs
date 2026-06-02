// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Options;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Resolves the configured source via <see cref="IPackageSourceProvider"/>,
/// builds a <see cref="NuGetProtocolSourceClient"/> for it, and delegates the
/// catalog operations workload commands need: search, version resolution,
/// and package download.
/// </summary>
internal sealed class WorkloadCatalog(IOptions<WorkloadCatalogOptions> options, IPackageSourceProvider sourceProvider, Func<PackageSource, NuGetProtocolSourceClient> clientFactory) : IWorkloadCatalog
{
    private readonly WorkloadCatalogOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly IPackageSourceProvider _sourceProvider = sourceProvider ?? throw new ArgumentNullException(nameof(sourceProvider));
    private readonly Func<PackageSource, NuGetProtocolSourceClient> _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));

    /// <inheritdoc />
    public Task<IReadOnlyList<CatalogSearchResult>> SearchAsync(CatalogSearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        CatalogSearchQuery effectiveQuery = query with { IncludePrerelease = IncludePrerelease(query.IncludePrerelease) };

        return ResolveClient(effectiveQuery.Source).SearchAsync(effectiveQuery, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ResolvedPackage?> ResolveLatestVersionAsync(string packageId, bool? includePrerelease, NuGetVersion? currentVersion = null,
        bool allowMajor = true, string? source = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        bool effectiveIncludePrerelease = IncludePrerelease(includePrerelease);

        return await ResolveMatchingVersionAsync(
            packageId,
            source,
            cancellationToken,
            versions => SelectLatest(
                versions,
                candidate => (effectiveIncludePrerelease || !candidate.IsPrerelease)
                    && (allowMajor || currentVersion is null || candidate.Major == currentVersion.Major)));
    }

    /// <inheritdoc />
    public async Task<ResolvedPackage?> ResolveLatestVersionInRangeAsync(string packageId, VersionRange versionRange, bool? includePrerelease, string? source = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(versionRange);
        bool effectiveIncludePrerelease = IncludePrerelease(includePrerelease);

        return await ResolveMatchingVersionAsync(packageId, source, cancellationToken,
            versions => SelectLatest(versions, candidate => SatisfiesRange(versionRange, candidate, effectiveIncludePrerelease)));
    }

    /// <inheritdoc />
    public async Task<ResolvedPackage?> ResolveVersionAsync(string packageId, NuGetVersion version, string? source = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(version);

        return await ResolveMatchingVersionAsync(packageId, source, cancellationToken,
            versions => versions.Any(candidate => candidate.Equals(version)) ? version : null);
    }

    /// <inheritdoc />
    public Task<Stream> DownloadAsync(ResolvedPackage package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        return _clientFactory(package.Source).OpenPackageAsync(package.PackageId, package.Version, cancellationToken);
    }

    private NuGetProtocolSourceClient ResolveClient(string? source)
        => _clientFactory(_sourceProvider.GetSource(source));

    private bool IncludePrerelease(bool? includePrerelease) => includePrerelease ?? _options.IncludePrerelease;

    private async Task<ResolvedPackage?> ResolveMatchingVersionAsync(string packageId, string? source, CancellationToken cancellationToken,
        Func<IReadOnlyList<NuGetVersion>, NuGetVersion?> selectVersion)
    {
        NuGetProtocolSourceClient client = ResolveClient(source);
        IReadOnlyList<NuGetVersion> versions = await client.ListVersionsAsync(packageId, cancellationToken);
        NuGetVersion? selected = selectVersion(versions);

        return selected is null ? null : new ResolvedPackage(packageId.ToLowerInvariant(), selected, client.Source);
    }

    private static NuGetVersion? SelectLatest(IEnumerable<NuGetVersion> versions, Func<NuGetVersion, bool> predicate)
    {
        NuGetVersion? best = null;
        foreach (NuGetVersion candidate in versions)
        {
            if (!predicate(candidate))
            {
                continue;
            }

            if (best is null || candidate > best)
            {
                best = candidate;
            }
        }

        return best;
    }

    private static bool SatisfiesRange(VersionRange range, NuGetVersion candidate, bool includePrerelease)
        => WorkloadVersionRanges.SatisfiesRange(range, candidate, includePrerelease);
}
