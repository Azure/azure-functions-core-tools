// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workers;

/// <summary>
/// Resolves worker runtimes required by workload project factories.
/// </summary>
internal sealed class DefaultFunctionsWorkerResolver(
    IWorkloadProvider workloadProvider,
    IWorkerConfigFileSystem workerConfigFileSystem) : IFunctionsWorkerResolver
{
    private const string WorkerPackageIdPrefix = "Azure.Functions.Cli.Workloads.Workers.";
    private const string WorkerConfigFileName = "worker.config.json";

    // Profiles will replace this lookup. A missing entry intentionally means "latest installed worker".
    private static readonly IReadOnlyDictionary<string, WorkerConstraint> _activeWorkerConstraints =
        new Dictionary<string, WorkerConstraint>(StringComparer.OrdinalIgnoreCase);

    private readonly IWorkloadProvider _workloadProvider = workloadProvider ?? throw new ArgumentNullException(nameof(workloadProvider));
    private readonly IWorkerConfigFileSystem _workerConfigFileSystem = workerConfigFileSystem ?? throw new ArgumentNullException(nameof(workerConfigFileSystem));

    public Task<FunctionsWorkerResolutionResult> ResolveWorkerAsync(FunctionsWorkerId workerId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workerId);
        cancellationToken.ThrowIfCancellationRequested();

        string packageId = GetWorkerPackageId(workerId);
        _activeWorkerConstraints.TryGetValue(workerId.Value, out WorkerConstraint? constraint);

        IReadOnlyList<ContentWorkloadInfo> installedWorkers = _workloadProvider.GetContentWorkloadsByPackageId(packageId);
        if (installedWorkers.Count == 0)
        {
            return Task.FromResult(NotInstalled(workerId));
        }

        List<InstalledWorkerCandidate> candidates = [];

        foreach (ContentWorkloadInfo workload in installedWorkers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!NuGetVersion.TryParse(workload.PackageVersion, out NuGetVersion? version)
                || !SatisfiesConstraint(version, constraint))
            {
                continue;
            }

            candidates.Add(new InstalledWorkerCandidate(workload, version));
        }

        if (candidates.Count == 0)
        {
            return Task.FromResult(MissingCompatibleVersion(workerId, constraint));
        }

        InstalledWorkerCandidate selected = candidates
            .OrderByDescending(c => c.Version)
            .First();

        string workerConfigPath = Path.Combine(selected.Workload.ContentRoot, WorkerConfigFileName);
        if (!_workerConfigFileSystem.FileExists(workerConfigPath))
        {
            return Task.FromResult(InvalidInstallation(workerId, selected, workerConfigPath));
        }

        IFunctionsWorker resolvedWorker = new ResolvedFunctionsWorker(
            workerId,
            workerId.Value,
            workerConfigPath,
            selected.Version.ToNormalizedString());

        return Task.FromResult(FunctionsWorkerResolutionResults.Resolved(resolvedWorker));
    }

    private static bool SatisfiesConstraint(NuGetVersion version, WorkerConstraint? constraint)
        => constraint is null || constraint.VersionRange.Satisfies(version);

    private static string GetWorkerPackageId(FunctionsWorkerId workerId) => WorkerPackageIdPrefix + workerId.Value;

    private static string GetWorkerInstallAlias(FunctionsWorkerId workerId) => workerId.Value + "-worker";

    private static FunctionsWorkerResolutionResult NotInstalled(FunctionsWorkerId workerId)
        => NotInstalledResult(
            workerId,
            $"No installed Azure Functions worker was found for '{workerId.Value}'. "
            + $"Run 'func workload install {GetWorkerInstallAlias(workerId)}' to install it.");

    private static FunctionsWorkerResolutionResult NotInstalledResult(FunctionsWorkerId workerId, string message)
    {
        FunctionsWorkerResolutionFailure failure = FunctionsWorkerResolutionFailures.NotInstalled(workerId, message);
        return FunctionsWorkerResolutionResults.NotResolved(failure);
    }

    private static FunctionsWorkerResolutionResult MissingCompatibleVersion(
        FunctionsWorkerId workerId,
        WorkerConstraint? constraint)
    {
        if (constraint is null)
        {
            FunctionsWorkerResolutionFailure invalidVersionFailure =
                FunctionsWorkerResolutionFailures.MissingCompatibleVersion(
                    workerId,
                    versionConstraint: null,
                    $"Installed Azure Functions worker workloads for '{workerId.Value}' do not include a valid package version. "
                    + $"Run 'func workload install {GetWorkerInstallAlias(workerId)} --force' to repair the install.");

            return FunctionsWorkerResolutionResults.NotResolved(invalidVersionFailure);
        }

        string rangeText = RangeText(constraint.VersionRange);
        FunctionsWorkerResolutionFailure failure = FunctionsWorkerResolutionFailures.MissingCompatibleVersion(
            workerId,
            rangeText,
            $"Installed Azure Functions worker workloads for '{workerId.Value}' do not satisfy version range '{rangeText}'. "
            + $"Run 'func workload install {GetWorkerInstallAlias(workerId)}' to install a compatible worker.");

        return FunctionsWorkerResolutionResults.NotResolved(failure);
    }

    private static FunctionsWorkerResolutionResult InvalidInstallation(
        FunctionsWorkerId workerId,
        InstalledWorkerCandidate selected,
        string workerConfigPath)
    {
        FunctionsWorkerResolutionFailure failure = FunctionsWorkerResolutionFailures.InvalidInstallation(
            workerId,
            selected.Workload.PackageId,
            selected.Version.ToNormalizedString(),
            workerConfigPath,
            $"Installed Azure Functions worker '{workerId.Value}' package '{selected.Workload.PackageId}' "
            + $"{selected.Version.ToNormalizedString()} is missing '{workerConfigPath}'. "
            + $"Run 'func workload install {GetWorkerInstallAlias(workerId)} --force' to repair the install.");

        return FunctionsWorkerResolutionResults.NotResolved(failure);
    }

    private static string RangeText(VersionRange range) => range.OriginalString ?? range.ToString();

    private sealed record WorkerConstraint(VersionRange VersionRange);

    private sealed record InstalledWorkerCandidate(ContentWorkloadInfo Workload, NuGetVersion Version);

    private sealed record ResolvedFunctionsWorker(
        FunctionsWorkerId Id,
        string WorkerRuntime,
        string WorkerConfigPath,
        string Version) : IFunctionsWorker;
}
