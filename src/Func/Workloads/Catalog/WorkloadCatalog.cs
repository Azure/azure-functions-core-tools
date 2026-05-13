// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Resolves the configured source via <see cref="IPackageSourceProvider"/>,
/// caches per-source <see cref="NuGetProtocolSourceClient"/> instances, and
/// delegates the catalog operations workload commands need: search,
/// version resolution, and package download.
/// </summary>
internal sealed class WorkloadCatalog(
    IPackageSourceProvider sourceProvider,
    Func<PackageSource, NuGetProtocolSourceClient> clientFactory) : IWorkloadCatalog
{
    private readonly IPackageSourceProvider _sourceProvider = sourceProvider ?? throw new ArgumentNullException(nameof(sourceProvider));
    private readonly Func<PackageSource, NuGetProtocolSourceClient> _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    private readonly ConcurrentDictionary<string, NuGetProtocolSourceClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<IReadOnlyList<CatalogSearchResult>> SearchAsync(
        CatalogSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ResolveClient(query.OverrideSource).SearchAsync(query, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ResolvedPackage?> ResolveLatestVersionAsync(
        string packageId,
        bool includePrerelease,
        NuGetVersion? currentVersion = null,
        bool allowMajor = true,
        string? overrideSource = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        NuGetProtocolSourceClient client = ResolveClient(overrideSource);
        IReadOnlyList<NuGetVersion> versions = await client.ListVersionsAsync(packageId, cancellationToken);

        NuGetVersion? best = null;
        foreach (NuGetVersion candidate in versions)
        {
            if (!includePrerelease && candidate.IsPrerelease)
            {
                continue;
            }

            if (!allowMajor && currentVersion is not null && candidate.Major != currentVersion.Major)
            {
                continue;
            }

            if (best is null || candidate > best)
            {
                best = candidate;
            }
        }

        return best is null ? null : new ResolvedPackage(packageId.ToLowerInvariant(), best, client.Source);
    }

    /// <inheritdoc />
    public async Task<ResolvedPackage?> ResolveVersionAsync(
        string packageId,
        NuGetVersion version,
        string? overrideSource = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(version);

        NuGetProtocolSourceClient client = ResolveClient(overrideSource);
        IReadOnlyList<NuGetVersion> versions = await client.ListVersionsAsync(packageId, cancellationToken);

        return versions.Any(v => v.Equals(version))
            ? new ResolvedPackage(packageId.ToLowerInvariant(), version, client.Source)
            : null;
    }

    /// <inheritdoc />
    public Task<Stream> DownloadAsync(ResolvedPackage package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        return GetOrCreateClient(package.Source).OpenPackageAsync(package.PackageId, package.Version, cancellationToken);
    }

    private NuGetProtocolSourceClient ResolveClient(string? overrideSource)
        => GetOrCreateClient(_sourceProvider.GetSource(overrideSource));

    private NuGetProtocolSourceClient GetOrCreateClient(PackageSource source)
        => _clients.GetOrAdd(source.Name, _ => _clientFactory(source));
}
