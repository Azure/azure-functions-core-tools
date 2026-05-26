// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Resolves installed host workloads and validates explicit host pins.
/// TODO: Logic here must be reviewed when we plug the host workload resolver into the real initialization workflow,
/// as some of the information we have in the demo runner may not be available at the time this resolver runs in the real workflow.
/// </summary>
internal sealed class DefaultHostWorkloadResolver(
    IWorkloadProvider workloadProvider,
    IWorkloadCatalog workloadCatalog) : IHostWorkloadResolver
{
    private const string HostAlias = "host";
    private const int AliasSearchTake = 50;

    private readonly IWorkloadProvider _workloadProvider = workloadProvider ?? throw new ArgumentNullException(nameof(workloadProvider));
    private readonly IWorkloadCatalog _workloadCatalog = workloadCatalog ?? throw new ArgumentNullException(nameof(workloadCatalog));

    public async Task<HostWorkloadResolution> ResolveAsync(HostWorkloadResolutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<InstalledHostCandidate> candidates = GetInstalledHostCandidates();
        if (!string.IsNullOrWhiteSpace(context.RequestedHostVersion))
        {
            return ResolveRequestedHost(context, candidates);
        }

        IReadOnlyList<InstalledHostCandidate> compatibleCandidates = FilterByRange(candidates, context.ProfileHostVersionRange);

        InstalledHostCandidate? selected = compatibleCandidates
            .OrderByDescending(static candidate => candidate.Version)
            .FirstOrDefault();

        if (selected is not null)
        {
            HostWorkloadResolution resolution = new HostWorkloadResolution.Installed(
                selected.Workload,
                selected.Version,
                ExplicitlyRequested: false);

            return resolution;
        }

        if (context.Offline)
        {
            return CreateOfflineInstallRequired(context.ProfileHostVersionRange);
        }

        ResolvedPackage package = await ResolveLatestInstallPackageAsync(context.ProfileHostVersionRange, cancellationToken);
        string installVersion = package.Version.ToNormalizedString();
        string message = context.ProfileHostVersionRange is null
            ? $"No installed host workload found. Host {installVersion} will be installed."
            : $"No installed host workload found in profile range {RangeText(context.ProfileHostVersionRange)}";

        return new HostWorkloadResolution.InstallRequired(installVersion, message);
    }

    private HostWorkloadResolution ResolveRequestedHost(
        HostWorkloadResolutionContext context,
        IReadOnlyList<InstalledHostCandidate> candidates)
    {
        if (!NuGetVersion.TryParse(context.RequestedHostVersion, out NuGetVersion? requestedVersion))
        {
            throw new HostWorkloadResolutionException(
                $"--host-version must be a valid NuGet version. Got '{context.RequestedHostVersion}'.");
        }

        if (context.ProfileHostVersionRange is { } range && !range.Satisfies(requestedVersion))
        {
            throw new HostWorkloadResolutionException(
                $"Requested host version '{requestedVersion.ToNormalizedString()}' is outside profile host range '{RangeText(range)}'.");
        }

        InstalledHostCandidate? selected = candidates.FirstOrDefault(candidate => VersionEquals(candidate.Version, requestedVersion));
        if (selected is not null)
        {
            return new HostWorkloadResolution.Installed(selected.Workload, selected.Version, ExplicitlyRequested: true);
        }

        string version = requestedVersion.ToNormalizedString();
        return new HostWorkloadResolution.InstallRequired(version, $"No installed host workload found for {version}");
    }

    private IReadOnlyList<InstalledHostCandidate> GetInstalledHostCandidates()
    {
        List<InstalledHostCandidate> candidates = [];
        foreach (ContentWorkloadInfo workload in _workloadProvider.GetContentWorkloads())
        {
            if (!workload.Aliases.Any(static alias => string.Equals(alias, HostAlias, StringComparison.OrdinalIgnoreCase))
                || !NuGetVersion.TryParse(workload.PackageVersion, out NuGetVersion? version))
            {
                continue;
            }

            candidates.Add(new InstalledHostCandidate(workload, version));
        }

        return candidates;
    }

    private static IReadOnlyList<InstalledHostCandidate> FilterByRange(IReadOnlyList<InstalledHostCandidate> candidates, VersionRange? range)
        => range is null
            ? candidates
            : [.. candidates.Where(candidate => range.Satisfies(candidate.Version))];

    private async Task<ResolvedPackage> ResolveLatestInstallPackageAsync(
        VersionRange? range,
        CancellationToken cancellationToken)
    {
        string packageId = await ResolveHostPackageIdAsync(cancellationToken);
        ResolvedPackage? package = range is null
            ? await _workloadCatalog.ResolveLatestVersionAsync(
                packageId, includePrerelease: false, currentVersion: null, allowMajor: true, source: null, cancellationToken)
            : await _workloadCatalog.ResolveLatestVersionInRangeAsync(
                packageId, range, includePrerelease: false, source: null, cancellationToken);

        if (package is not null)
        {
            return package;
        }

        string message = range is null
            ? "No host workload version is available from the configured workload catalog."
            : $"No host workload version in profile range '{RangeText(range)}' is available from the configured workload catalog.";
        throw new HostWorkloadResolutionException(message);
    }

    private async Task<string> ResolveHostPackageIdAsync(CancellationToken cancellationToken)
    {
        var query = new CatalogSearchQuery
        {
            Filter = HostAlias,
            IncludePrerelease = false,
            Take = AliasSearchTake,
        };

        IReadOnlyList<CatalogSearchResult> hits = await _workloadCatalog.SearchAsync(query, cancellationToken);
        IReadOnlyList<string> matchedIds = FilterByAlias(hits, HostAlias);

        if (matchedIds.Count == 0)
        {
            IReadOnlyList<CatalogSearchResult> all = await _workloadCatalog.SearchAsync(query with { Filter = null }, cancellationToken);
            matchedIds = FilterByAlias(all, HostAlias);
        }

        if (matchedIds.Count == 0)
        {
            throw new HostWorkloadResolutionException("No host workload package was found in the configured workload catalog.");
        }

        if (matchedIds.Count > 1)
        {
            string matches = string.Join(", ", matchedIds);
            throw new HostWorkloadResolutionException($"Multiple host workload packages were found: {matches}.");
        }

        return matchedIds[0];
    }

    private static HostWorkloadResolution CreateOfflineInstallRequired(VersionRange? range)
    {
        string version = range is null ? "latest" : RangeText(range);
        string message = range is null
            ? "No installed host workload found"
            : $"No installed host workload found in profile range {RangeText(range)}";

        return new HostWorkloadResolution.InstallRequired(version, message);
    }

    private static IReadOnlyList<string> FilterByAlias(IReadOnlyList<CatalogSearchResult> hits, string alias)
        => [.. hits
            .Where(result => result.Aliases.Any(candidate => string.Equals(candidate, alias, StringComparison.OrdinalIgnoreCase)))
            .Select(result => result.PackageId)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static bool VersionEquals(NuGetVersion left, NuGetVersion right)
        => left.Equals(right);

    private static string RangeText(VersionRange range) => range.OriginalString ?? range.ToString();

    private sealed record InstalledHostCandidate(ContentWorkloadInfo Workload, NuGetVersion Version);
}
