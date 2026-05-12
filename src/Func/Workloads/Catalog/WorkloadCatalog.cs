// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Aggregates one or more <see cref="ISourceClient"/>s into the
/// catalog-shaped operations workload commands need: search, latest-version
/// resolution, and package download. Search and resolve query every source
/// in parallel. Download targets the source the package was resolved on; a
/// stale <see cref="ResolvedPackage"/> is the caller's problem to re-resolve.
/// </summary>
internal sealed class WorkloadCatalog(
    IPackageSourceProvider sourceProvider,
    Func<PackageSource, ISourceClient> clientFactory) : IWorkloadCatalog
{
    private readonly IPackageSourceProvider _sourceProvider = sourceProvider ?? throw new ArgumentNullException(nameof(sourceProvider));
    private readonly Func<PackageSource, ISourceClient> _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    private readonly ConcurrentDictionary<string, ISourceClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CatalogSearchResult>> SearchAsync(
        string? query,
        bool includePrerelease,
        int skip,
        int take,
        string? overrideSource = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);

        IReadOnlyList<ISourceClient> clients = ResolveClients(overrideSource);

        IReadOnlyList<CatalogSearchResult>[] perSource = await Task.WhenAll(
            clients.Select(c => c.SearchAsync(query, includePrerelease, skip, take, cancellationToken)));

        var byId = new Dictionary<string, CatalogSearchResult>(StringComparer.OrdinalIgnoreCase);
        foreach (IReadOnlyList<CatalogSearchResult> bucket in perSource)
        {
            foreach (CatalogSearchResult entry in bucket)
            {
                if (byId.TryGetValue(entry.PackageId, out CatalogSearchResult? existing) && existing.LatestVersion >= entry.LatestVersion)
                {
                    continue;
                }

                byId[entry.PackageId] = entry;
            }
        }

        return byId.Values
            .OrderByDescending(r => r.LatestVersion)
            .ThenBy(r => r.PackageId, StringComparer.Ordinal)
            .Take(take)
            .ToList();
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

        IReadOnlyList<ISourceClient> clients = ResolveClients(overrideSource);

        IReadOnlyList<NuGetVersion>[] perSource = await Task.WhenAll(
            clients.Select(c => c.ListVersionsAsync(packageId, cancellationToken)));

        ResolvedPackage? best = null;
        for (int i = 0; i < clients.Count; i++)
        {
            ISourceClient client = clients[i];
            foreach (NuGetVersion candidate in perSource[i])
            {
                if (!includePrerelease && candidate.IsPrerelease)
                {
                    continue;
                }

                if (!allowMajor && currentVersion is not null && candidate.Major != currentVersion.Major)
                {
                    continue;
                }

                if (best is null || candidate > best.Version)
                {
                    best = new ResolvedPackage(packageId.ToLowerInvariant(), candidate, client.Source);
                }
            }
        }

        return best;
    }

    /// <inheritdoc />
    public Task<Stream> DownloadAsync(ResolvedPackage package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        return GetOrCreateClient(package.Source).OpenPackageAsync(package.PackageId, package.Version, cancellationToken);
    }

    private IReadOnlyList<ISourceClient> ResolveClients(string? overrideSource)
    {
        IReadOnlyList<PackageSource> sources = _sourceProvider.GetSources(overrideSource);
        var clients = new List<ISourceClient>(sources.Count);
        foreach (PackageSource source in sources)
        {
            clients.Add(GetOrCreateClient(source));
        }

        return clients;
    }

    private ISourceClient GetOrCreateClient(PackageSource source)
        => _clients.GetOrAdd(source.Name, _ => _clientFactory(source));
}
