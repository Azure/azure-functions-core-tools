// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Aggregates one or more <see cref="NuGetProtocolSourceClient"/>s into the
/// catalog-shaped operations workload commands need: search, latest-version
/// resolution, and package download. Search and resolve query every source
/// in parallel. Download targets the source the package was resolved on; a
/// stale <see cref="ResolvedPackage"/> is the caller's problem to re-resolve.
/// </summary>
internal sealed class WorkloadCatalog(
    IPackageSourceProvider sourceProvider,
    Func<PackageSource, NuGetProtocolSourceClient> clientFactory) : IWorkloadCatalog
{
    private readonly IPackageSourceProvider _sourceProvider = sourceProvider ?? throw new ArgumentNullException(nameof(sourceProvider));
    private readonly Func<PackageSource, NuGetProtocolSourceClient> _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    private readonly ConcurrentDictionary<string, NuGetProtocolSourceClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public AsyncPageable<CatalogSearchResult> Search(
        CatalogSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new AggregateSearchPageable(this, query, cancellationToken);
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

        IReadOnlyList<NuGetProtocolSourceClient> clients = ResolveClients(overrideSource);

        IReadOnlyList<NuGetVersion>[] perSource = await Task.WhenAll(
            clients.Select(c => c.ListVersionsAsync(packageId, cancellationToken)));

        ResolvedPackage? best = null;
        for (int i = 0; i < clients.Count; i++)
        {
            NuGetProtocolSourceClient client = clients[i];
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
    public async Task<ResolvedPackage?> ResolveVersionAsync(
        string packageId,
        NuGetVersion version,
        string? overrideSource = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(version);

        IReadOnlyList<NuGetProtocolSourceClient> clients = ResolveClients(overrideSource);

        // Probe in source precedence order so the first configured source wins
        // (matches the precedence in IPackageSourceProvider: CLI > env > nuget.org).
        foreach (NuGetProtocolSourceClient client in clients)
        {
            IReadOnlyList<NuGetVersion> versions = await client.ListVersionsAsync(packageId, cancellationToken);
            if (versions.Any(v => v.Equals(version)))
            {
                return new ResolvedPackage(packageId.ToLowerInvariant(), version, client.Source);
            }
        }

        return null;
    }

    /// <inheritdoc />
    public Task<Stream> DownloadAsync(ResolvedPackage package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        return GetOrCreateClient(package.Source).OpenPackageAsync(package.PackageId, package.Version, cancellationToken);
    }

    private IReadOnlyList<NuGetProtocolSourceClient> ResolveClients(string? overrideSource)
    {
        IReadOnlyList<PackageSource> sources = _sourceProvider.GetSources(overrideSource);
        var clients = new List<NuGetProtocolSourceClient>(sources.Count);
        foreach (PackageSource source in sources)
        {
            clients.Add(GetOrCreateClient(source));
        }

        return clients;
    }

    private NuGetProtocolSourceClient GetOrCreateClient(PackageSource source)
        => _clients.GetOrAdd(source.Name, _ => _clientFactory(source));

    private sealed class AggregateSearchPageable(
        WorkloadCatalog owner,
        CatalogSearchQuery query,
        CancellationToken cancellationToken) : AsyncPageable<CatalogSearchResult>(cancellationToken)
    {
        private readonly WorkloadCatalog _owner = owner;
        private readonly CatalogSearchQuery _query = query;

        public override async IAsyncEnumerable<Page<CatalogSearchResult>> AsPages(
            string? continuationToken = null,
            int? pageSizeHint = null)
        {
            int pageSize = pageSizeHint
                ?? _query.PageSize
                ?? CatalogSearchQuery.DefaultPageSize;
            int offset = int.TryParse(continuationToken ?? _query.ContinuationToken, out int parsed) ? parsed : 0;

            // Aggregator semantics force eager materialisation: dedup-by-id
            // (highest version wins) and sort are global operations across
            // sources, so we drain each per-source pageable before chunking.
            IReadOnlyList<CatalogSearchResult> all = await MaterialiseAsync().ConfigureAwait(false);

            while (offset < all.Count)
            {
                int remaining = all.Count - offset;
                int take = Math.Min(pageSize, remaining);
                var slice = new List<CatalogSearchResult>(take);
                for (int i = 0; i < take; i++)
                {
                    slice.Add(all[offset + i]);
                }

                offset += take;
                string? nextToken = offset < all.Count ? offset.ToString() : null;
                yield return Page<CatalogSearchResult>.FromValues(slice, nextToken, response: null!);

                if (nextToken is null)
                {
                    yield break;
                }
            }
        }

        private async Task<IReadOnlyList<CatalogSearchResult>> MaterialiseAsync()
        {
            IReadOnlyList<NuGetProtocolSourceClient> clients = _owner.ResolveClients(_query.OverrideSource);

            // Drive each per-source pageable to exhaustion in parallel. We pass
            // a fresh CatalogSearchQuery without our own continuation token so
            // each source starts from the beginning.
            CatalogSearchQuery perSourceQuery = _query with { ContinuationToken = null };
            IEnumerable<CatalogSearchResult>[] perSource = await Task.WhenAll(
                clients.Select(c => DrainAsync(c.Search(perSourceQuery, CancellationToken))));

            var byId = new Dictionary<string, CatalogSearchResult>(StringComparer.OrdinalIgnoreCase);
            foreach (IEnumerable<CatalogSearchResult> bucket in perSource)
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
                .ToList();
        }

        private static async Task<IEnumerable<CatalogSearchResult>> DrainAsync(AsyncPageable<CatalogSearchResult> pageable)
        {
            List<CatalogSearchResult> results = [];
            await foreach (CatalogSearchResult item in pageable.ConfigureAwait(false))
            {
                results.Add(item);
            }

            return results;
        }
    }
}
