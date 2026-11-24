// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.Extensions.Options;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Install;

internal sealed class WorkloadPackageSource(IWorkloadCatalog catalog, IWorkloadPackageInspector packageInspector,
    IOptions<WorkloadCatalogOptions> catalogOptions) : IWorkloadPackageSource
{
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
        if (!exact && await FindAliasAsync(packageId, version, source, effectiveIncludePrerelease, cancellationToken) is { } alias)
        {
            return alias;
        }

        ResolvedPackage? resolved = version is null
            ? await _catalog.ResolveLatestVersionAsync(
                packageId, effectiveIncludePrerelease, currentVersion: null, allowMajor: true, source, cancellationToken)
            : await _catalog.ResolveVersionAsync(packageId, version, source, cancellationToken);
        if (resolved is not null)
        {
            return resolved;
        }

        string detail = version is null
            ? "No matching version was found on any configured source."
            : $"Version '{version.ToNormalizedString()}' was not found on any configured source.";
        string hint = effectiveIncludePrerelease ? string.Empty : " Pass --prerelease if it is a prerelease.";
        throw new WorkloadPackageNotFoundException($"Could not resolve workload '{packageId}'. {detail}{hint}");
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

    private async Task<ResolvedPackage?> FindAliasAsync(string alias, NuGetVersion? version, string? source, bool includePrerelease, CancellationToken cancellationToken)
    {
        // Keyword search indexes the latest package, not every alias (especially historical aliases).
        CatalogSearchQuery query = new() { Source = source, IncludePrerelease = includePrerelease || version?.IsPrerelease == true };
        IReadOnlyList<CatalogSearchResult> rows = version is null
            ? await _catalog.SearchAsync(query, cancellationToken)
            : await _catalog.SearchVersionsAsync(query, cancellationToken);
        List<CatalogSearchResult> matches = [.. rows.Where(row => (version is null || row.LatestVersion == version)
            && row.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))];

        // Some feeds return per-RID content alongside its user-facing pointer. Keep
        // the content-install policy separate from stack selection: a package's
        // name is never evidence that two declared stack owners are the same.
        if (matches.All(row => row.Stack is null) && matches.Any(row => string.Equals(row.Kind, "rid-pointer", StringComparison.OrdinalIgnoreCase)))
        {
            matches.RemoveAll(row => row.Rid is not null && string.Equals(row.Kind, "content", StringComparison.OrdinalIgnoreCase));
        }
        return matches switch
        {
            [] => null,
            [var match] => new ResolvedPackage(match.PackageId, match.LatestVersion, match.Source),
            _ => throw new AmbiguousPackageMatchException(alias, [.. matches.Select(row => row.PackageId)]),
        };
    }

    private bool IncludePrerelease(bool? includePrerelease)
        => includePrerelease ?? _catalogOptions.IncludePrerelease;

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
