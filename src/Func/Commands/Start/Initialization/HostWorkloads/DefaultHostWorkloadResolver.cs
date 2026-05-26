// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Resolves installed host workloads and validates explicit host pins for the current runtime RID.
/// </summary>
internal sealed class DefaultHostWorkloadResolver(
    IWorkloadProvider workloadProvider,
    IWorkloadCatalog workloadCatalog) : IHostWorkloadResolver
{
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

        return new HostWorkloadResolution.InstallRequired(installVersion, message, package.PackageId);
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
        if (context.Offline)
        {
            return new HostWorkloadResolution.InstallRequired(version, $"No installed host workload found for {version}");
        }

        return new HostWorkloadResolution.InstallRequired(
            version,
            $"No installed host workload found for {version}",
            HostWorkloadPackage.CurrentPackageId);
    }

    private IReadOnlyList<InstalledHostCandidate> GetInstalledHostCandidates()
    {
        List<InstalledHostCandidate> candidates = [];
        foreach (ContentWorkloadInfo workload in _workloadProvider.GetContentWorkloads())
        {
            if (!string.Equals(workload.PackageId, HostWorkloadPackage.CurrentPackageId, StringComparison.OrdinalIgnoreCase)
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
        string packageId = HostWorkloadPackage.CurrentPackageId;
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

    private static HostWorkloadResolution CreateOfflineInstallRequired(VersionRange? range)
    {
        string version = range is null ? "latest" : RangeText(range);
        string message = range is null
            ? "No installed host workload found"
            : $"No installed host workload found in profile range {RangeText(range)}";

        return new HostWorkloadResolution.InstallRequired(version, message);
    }

    private static bool VersionEquals(NuGetVersion left, NuGetVersion right)
        => left.Equals(right);

    private static string RangeText(VersionRange range) => range.OriginalString ?? range.ToString();

    private sealed record InstalledHostCandidate(ContentWorkloadInfo Workload, NuGetVersion Version);
}
