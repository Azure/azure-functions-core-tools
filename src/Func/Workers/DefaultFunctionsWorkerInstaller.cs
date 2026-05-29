// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workers;

/// <summary>
/// Default installer for Functions worker content workloads.
/// </summary>
internal sealed class DefaultFunctionsWorkerInstaller(
    IWorkloadCatalog workloadCatalog,
    IWorkloadInstaller workloadInstaller,
    IWorkloadPaths workloadPaths,
    IFunctionsWorkerContentResolver workerContentResolver) : IFunctionsWorkerInstaller
{
    private readonly IWorkloadCatalog _workloadCatalog = workloadCatalog ?? throw new ArgumentNullException(nameof(workloadCatalog));
    private readonly IWorkloadInstaller _workloadInstaller = workloadInstaller ?? throw new ArgumentNullException(nameof(workloadInstaller));
    private readonly IWorkloadPaths _workloadPaths = workloadPaths ?? throw new ArgumentNullException(nameof(workloadPaths));
    private readonly IFunctionsWorkerContentResolver _workerContentResolver = workerContentResolver
        ?? throw new ArgumentNullException(nameof(workerContentResolver));

    public async Task<FunctionsWorkerInstallResult> InstallAsync(
        FunctionsWorkerId workerId,
        IReadOnlyDictionary<string, VersionRange> workerVersionRanges,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workerId);
        ArgumentNullException.ThrowIfNull(workerVersionRanges);
        cancellationToken.ThrowIfCancellationRequested();

        string packageId = FunctionsWorkerWorkloadPackages.GetPackageId(workerId);
        workerVersionRanges.TryGetValue(workerId.Value, out VersionRange? versionRange);

        NuGetVersion? version = null;
        string? source = null;
        if (versionRange is not null)
        {
            ResolvedPackage? package = await _workloadCatalog.ResolveLatestVersionInRangeAsync(
                packageId,
                versionRange,
                includePrerelease: true,
                source: null,
                cancellationToken);

            if (package is null)
            {
                string rangeText = versionRange.OriginalString ?? versionRange.ToString();
                throw new WorkloadPackageNotFoundException($"No worker workload package '{packageId}' satisfies profile range '{rangeText}'.");
            }

            version = package.Version;
            source = package.Source.Source;
        }

        WorkloadInstallResult result = await _workloadInstaller.InstallFromCatalogAsync(
            packageId,
            version,
            source,
            includePrerelease: true,
            exact: true,
            force: false,
            progress: null,
            cancellationToken);

        IFunctionsWorker worker = ResolveInstalledWorker(workerId, packageId, versionRange, result, cancellationToken);
        return new FunctionsWorkerInstallResult(worker, result);
    }

    private IFunctionsWorker ResolveInstalledWorker(
        FunctionsWorkerId workerId,
        string expectedPackageId,
        VersionRange? versionRange,
        WorkloadInstallResult result,
        CancellationToken cancellationToken)
    {
        WorkloadEntry entry = result.Entry;
        if (!string.Equals(entry.PackageId, expectedPackageId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidWorkloadException(
                $"Expected worker workload package '{expectedPackageId}' but installed package '{entry.PackageId}'.");
        }

        if (entry.Kind != WorkloadKind.Content)
        {
            throw new InvalidWorkloadException(
                $"Expected worker workload package '{entry.PackageId}' to be kind 'content' but found '{entry.Kind.ToString().ToLowerInvariant()}'.");
        }

        ContentWorkloadInfo installedWorker = CreateContentWorkloadInfo(entry);
        FunctionsWorkerResolutionResult resolution = _workerContentResolver.ResolveWorker(
            workerId,
            [installedWorker],
            versionRange,
            cancellationToken);

        return resolution is FunctionsWorkerResolutionResult.Resolved resolved
            ? resolved.Worker
            : throw CreateInvalidInstalledWorkerException((FunctionsWorkerResolutionResult.NotResolved)resolution);
    }

    private ContentWorkloadInfo CreateContentWorkloadInfo(WorkloadEntry entry)
    {
        string installDirectory = _workloadPaths.GetInstallDirectory(entry.PackageId, entry.PackageVersion);
        string contentRoot = Path.GetFullPath(Path.Combine(installDirectory, "tools", "any"));

        return new ContentWorkloadInfo(
            entry.PackageId,
            entry.PackageVersion,
            entry.Aliases,
            installDirectory,
            contentRoot,
            entry.DisplayName,
            entry.Description);
    }

    private static InvalidWorkloadException CreateInvalidInstalledWorkerException(FunctionsWorkerResolutionResult.NotResolved notResolved)
        => new(notResolved.Failure.Message);
}
