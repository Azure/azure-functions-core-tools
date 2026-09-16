// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.Extensions.Options;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Install;

internal sealed class WorkloadPackageSource(IWorkloadCatalog catalog, IWorkloadPackageInspector packageInspector,
    IOptions<WorkloadCatalogOptions> catalogOptions) : IWorkloadPackageSource
{
    private const int AliasSearchTake = 50;

    private readonly IWorkloadCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly IWorkloadPackageInspector _packageInspector =
        packageInspector ?? throw new ArgumentNullException(nameof(packageInspector));
    private readonly WorkloadCatalogOptions _catalogOptions =
        catalogOptions?.Value ?? throw new ArgumentNullException(nameof(catalogOptions));

    public async Task<ResolvedPackage> ResolveAsync(string packageId, NuGetVersion? version, string? source,
        bool? includePrerelease, bool exact, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        bool effectiveIncludePrerelease = IncludePrerelease(includePrerelease);
        string resolvedId = exact
            ? packageId
            : await ResolveAliasOrIdAsync(packageId, source, effectiveIncludePrerelease, cancellationToken);
        ResolvedPackage? resolved = version is null
            ? await _catalog.ResolveLatestVersionAsync(
                resolvedId, effectiveIncludePrerelease, currentVersion: null, allowMajor: true, source, cancellationToken)
            : await _catalog.ResolveVersionAsync(resolvedId, version, source, cancellationToken);
        if (resolved is not null)
        {
            return resolved;
        }

        string detail = version is null
            ? "No matching version was found on any configured source."
            : $"Version '{version.ToNormalizedString()}' was not found on any configured source.";
        string hint = effectiveIncludePrerelease ? string.Empty : " Pass --prerelease if it is a prerelease.";
        throw new WorkloadPackageNotFoundException($"Could not resolve workload '{resolvedId}'. {detail}{hint}");
    }

    public Task<ResolvedPackage?> ResolveLatestVersionAsync(string packageId, bool? includePrerelease, NuGetVersion currentVersion,
        bool allowMajor, string? source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(currentVersion);

        return _catalog.ResolveLatestVersionAsync(packageId, IncludePrerelease(includePrerelease), currentVersion, allowMajor, source, cancellationToken);
    }

    public async Task<ResolvedPackage> ResolveImplementationAsync(InspectedWorkloadPackage pointer,
        WorkloadPointerSelection selection, string source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var version = NuGetVersion.Parse(pointer.Identity.Version);
        ResolvedPackage? resolved = await _catalog.ResolveVersionAsync(selection.PackageId, version, source, cancellationToken);
        if (resolved is not null)
        {
            return resolved;
        }

        throw new WorkloadPackageNotFoundException(
            $"RID pointer '{pointer.Identity.PackageId}' {pointer.Identity.Version} selected runtime identifier " +
            $"'{selection.RuntimeIdentifier}', but implementation '{selection.PackageId}' {pointer.Identity.Version} " +
            $"was not found on source '{source}'. This may be a partial publish or feed indexing delay.");
    }

    public string FindLocalImplementation(string pointerPath, string packageId, string version, string runtimeIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pointerPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentifier);

        string directory = Path.GetDirectoryName(pointerPath)!;
        string expectedFileName = $"{packageId}.{version}.nupkg";
        IOrderedEnumerable<string> candidates = Directory
            .EnumerateFiles(directory, "*.nupkg", SearchOption.TopDirectoryOnly)
            .OrderByDescending(c => string.Equals(Path.GetFileName(c), expectedFileName, StringComparison.OrdinalIgnoreCase));
        foreach (string candidate in candidates)
        {
            if (string.Equals(candidate, pointerPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (_packageInspector.MatchesIdentity(candidate, packageId, version))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"Local RID pointer '{Path.GetFileName(pointerPath)}' requires implementation '{packageId}' {version} for RID " +
            $"'{runtimeIdentifier}' in directory '{directory}'. No configured feed was searched.");
    }

    public async Task<TemporaryWorkloadPackageFile> DownloadAsync(ResolvedPackage package, IProgress<WorkloadInstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        progress?.Report(new WorkloadInstallProgress(
            WorkloadInstallPhase.Downloading,
            $"Downloading '{package.PackageId}' {package.Version.ToNormalizedString()}"));
        string path = Path.Combine(Path.GetTempPath(), $"func-workload-{Guid.NewGuid():N}.nupkg");
        try
        {
            await using Stream packageStream = await _catalog.DownloadAsync(package, cancellationToken);
            await using FileStream tempStream = File.Create(path);
            await packageStream.CopyToAsync(tempStream, cancellationToken);
            return new TemporaryWorkloadPackageFile(path);
        }
        catch
        {
            TryDeleteFile(path);
            throw;
        }
    }

    public bool IsLocal(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        if (Path.IsPathFullyQualified(source))
        {
            return true;
        }

        return Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && uri.IsFile;
    }

    private async Task<string> ResolveAliasOrIdAsync(string aliasOrId, string? source, bool includePrerelease,
        CancellationToken cancellationToken)
    {
        CatalogSearchQuery query = new()
        {
            Filter = aliasOrId,
            IncludePrerelease = includePrerelease,
            Take = AliasSearchTake,
            Source = source,
        };

        IReadOnlyList<CatalogSearchResult> hits = await _catalog.SearchAsync(query, cancellationToken);
        IReadOnlyList<CatalogSearchResult> aliasMatches = FilterByAlias(hits, aliasOrId);
        if (aliasMatches.Count == 0 && hits.Count == 0)
        {
            IReadOnlyList<CatalogSearchResult> all = await _catalog.SearchAsync(query with { Filter = null }, cancellationToken);
            aliasMatches = FilterByAlias(all, aliasOrId);
        }

        IReadOnlyList<string> pointerIds = DistinctPackageIds(aliasMatches.Where(m =>
            string.Equals(m.Kind, "rid-pointer", StringComparison.OrdinalIgnoreCase)));
        if (pointerIds.Count == 1)
        {
            return pointerIds[0];
        }

        if (pointerIds.Count > 1)
        {
            throw new AmbiguousPackageMatchException(aliasOrId, pointerIds);
        }

        IReadOnlyList<string> matchedIds = DistinctPackageIds(aliasMatches);
        if (matchedIds.Count > 1)
        {
            throw new AmbiguousPackageMatchException(aliasOrId, matchedIds);
        }

        return matchedIds.Count == 1 ? matchedIds[0] : aliasOrId;
    }

    private bool IncludePrerelease(bool? includePrerelease)
        => includePrerelease ?? _catalogOptions.IncludePrerelease;

    private static IReadOnlyList<CatalogSearchResult> FilterByAlias(IReadOnlyList<CatalogSearchResult> hits, string alias)
        => [.. hits.Where(r => r.Aliases.Any(a => string.Equals(a, alias, StringComparison.OrdinalIgnoreCase)))];

    private static IReadOnlyList<string> DistinctPackageIds(IEnumerable<CatalogSearchResult> hits)
        => [.. hits.Select(r => r.PackageId).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Failed downloads are already surfacing their original error; temporary cleanup is best-effort.
        }
    }
}
