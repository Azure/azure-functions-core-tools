// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.Extensions.Options;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workers;

/// <summary>
/// Default worker resolver for content workload payloads.
/// </summary>
internal sealed class DefaultFunctionsWorkerContentResolver(
    IWorkerConfigFileSystem workerConfigFileSystem,
    IOptions<WorkloadCatalogOptions> catalogOptions) : IFunctionsWorkerContentResolver
{
    private const string WorkerConfigFileName = "worker.config.json";

    private readonly IWorkerConfigFileSystem _workerConfigFileSystem = workerConfigFileSystem
        ?? throw new ArgumentNullException(nameof(workerConfigFileSystem));

    private readonly WorkloadCatalogOptions _catalogOptions = catalogOptions?.Value
        ?? throw new ArgumentNullException(nameof(catalogOptions));

    public FunctionsWorkerResolutionResult ResolveWorker(
        FunctionsWorkerId workerId,
        IReadOnlyList<ContentWorkloadInfo> installedWorkers,
        VersionRange? versionConstraint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workerId);
        ArgumentNullException.ThrowIfNull(installedWorkers);
        cancellationToken.ThrowIfCancellationRequested();

        if (installedWorkers.Count == 0)
        {
            return NotInstalled(workerId);
        }

        List<InstalledWorkerCandidate> candidates = [];
        foreach (ContentWorkloadInfo workload in installedWorkers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!NuGetVersion.TryParse(workload.PackageVersion, out NuGetVersion? version)
                || !SatisfiesConstraint(version, versionConstraint))
            {
                continue;
            }

            candidates.Add(new InstalledWorkerCandidate(workload, version));
        }

        if (candidates.Count == 0)
        {
            return MissingCompatibleVersion(workerId, versionConstraint);
        }

        InstalledWorkerCandidate selected = candidates
            .OrderByDescending(c => c.Version)
            .First();

        string workerConfigPath = Path.Combine(selected.Workload.ContentRoot, WorkerConfigFileName);
        if (!_workerConfigFileSystem.FileExists(workerConfigPath))
        {
            return InvalidInstallation(workerId, selected, workerConfigPath);
        }

        IFunctionsWorker resolvedWorker = new ResolvedFunctionsWorker(
            workerId,
            workerId.Value,
            workerConfigPath,
            selected.Version.ToNormalizedString());

        return FunctionsWorkerResolutionResults.Resolved(resolvedWorker);
    }

    private bool SatisfiesConstraint(NuGetVersion version, VersionRange? constraint)
        => constraint is null
            || WorkloadVersionRanges.SatisfiesRange(constraint, version, _catalogOptions.IncludePrerelease);

    private static FunctionsWorkerResolutionResult NotInstalled(FunctionsWorkerId workerId)
        => NotInstalledResult(
            workerId,
            $"No installed Azure Functions worker was found for '{workerId.Value}'. "
            + $"Run '{FunctionsWorkerWorkloadPackages.GetInstallCommand(workerId)}' to install it.");

    private static FunctionsWorkerResolutionResult NotInstalledResult(FunctionsWorkerId workerId, string message)
    {
        FunctionsWorkerResolutionFailure failure = FunctionsWorkerResolutionFailures.NotInstalled(workerId, message);
        return FunctionsWorkerResolutionResults.NotResolved(failure);
    }

    private static FunctionsWorkerResolutionResult MissingCompatibleVersion(FunctionsWorkerId workerId, VersionRange? constraint)
    {
        if (constraint is null)
        {
            FunctionsWorkerResolutionFailure invalidVersionFailure =
                FunctionsWorkerResolutionFailures.MissingCompatibleVersion(
                    workerId,
                    versionConstraint: null,
                    $"Installed Azure Functions worker workloads for '{workerId.Value}' do not include a valid package version. "
                    + $"Run '{FunctionsWorkerWorkloadPackages.GetRepairCommand(workerId)}' to repair the install.");

            return FunctionsWorkerResolutionResults.NotResolved(invalidVersionFailure);
        }

        string rangeText = RangeText(constraint);
        FunctionsWorkerResolutionFailure failure = FunctionsWorkerResolutionFailures.MissingCompatibleVersion(
            workerId,
            rangeText,
            $"Installed Azure Functions worker workloads for '{workerId.Value}' do not satisfy version range '{rangeText}'. "
            + $"Run '{FunctionsWorkerWorkloadPackages.GetInstallCommand(workerId)}' to install a compatible worker.");

        return FunctionsWorkerResolutionResults.NotResolved(failure);
    }

    private static FunctionsWorkerResolutionResult InvalidInstallation(FunctionsWorkerId workerId, InstalledWorkerCandidate selected, string workerConfigPath)
    {
        FunctionsWorkerResolutionFailure failure = FunctionsWorkerResolutionFailures.InvalidInstallation(
            workerId,
            selected.Workload.PackageId,
            selected.Version.ToNormalizedString(),
            workerConfigPath,
            $"Installed Azure Functions worker '{workerId.Value}' package '{selected.Workload.PackageId}' "
            + $"{selected.Version.ToNormalizedString()} is missing '{workerConfigPath}'. "
            + $"Run '{FunctionsWorkerWorkloadPackages.GetRepairCommand(workerId)}' to repair the install.");

        return FunctionsWorkerResolutionResults.NotResolved(failure);
    }

    private static string RangeText(VersionRange range) => range.OriginalString ?? range.ToString();

    private sealed record InstalledWorkerCandidate(ContentWorkloadInfo Workload, NuGetVersion Version);

    private sealed record ResolvedFunctionsWorker(FunctionsWorkerId Id, string WorkerRuntime, string WorkerConfigPath, string Version) : IFunctionsWorker;
}
