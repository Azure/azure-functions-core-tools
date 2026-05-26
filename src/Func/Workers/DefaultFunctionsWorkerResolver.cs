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
    IWorkerConfigFileSystem workerConfigFileSystem,
    IReadOnlyDictionary<string, VersionRange>? activeWorkerConstraints = null) : IFunctionsWorkerResolver
{
    private const string WorkerPackageIdPrefix = "Azure.Functions.Cli.Workloads.Workers.";
    private const string WorkerConfigFileName = "worker.config.json";

    private readonly IWorkloadProvider _workloadProvider = workloadProvider ?? throw new ArgumentNullException(nameof(workloadProvider));
    private readonly IWorkerConfigFileSystem _workerConfigFileSystem = workerConfigFileSystem
        ?? throw new ArgumentNullException(nameof(workerConfigFileSystem));
    private readonly IReadOnlyDictionary<string, VersionRange> _activeWorkerConstraints =
        activeWorkerConstraints is null
            ? new Dictionary<string, VersionRange>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, VersionRange>(activeWorkerConstraints, StringComparer.OrdinalIgnoreCase);

    public Task<FunctionsWorkerResolutionResult> ResolveWorkerAsync(FunctionsWorkerId workerId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workerId);
        cancellationToken.ThrowIfCancellationRequested();

        _activeWorkerConstraints.TryGetValue(workerId.Value, out VersionRange? constraint);

        IReadOnlyList<ContentWorkloadInfo> installedWorkers = GetWorkerWorkloads(workerId);
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

    private IReadOnlyList<ContentWorkloadInfo> GetWorkerWorkloads(FunctionsWorkerId workerId)
    {
        string packageId = GetWorkerPackageId(workerId);
        string alias = GetWorkerInstallAlias(workerId);
        List<ContentWorkloadInfo> workloads = [];
        AddDistinct(workloads, _workloadProvider.GetContentWorkloadsByPackageId(packageId));
        AddDistinct(
            workloads,
            _workloadProvider.GetContentWorkloads()
                .Where(workload => workload.Aliases.Any(candidate => string.Equals(candidate, alias, StringComparison.OrdinalIgnoreCase))));
        return workloads;
    }

    private static void AddDistinct(List<ContentWorkloadInfo> target, IEnumerable<ContentWorkloadInfo> candidates)
    {
        foreach (ContentWorkloadInfo candidate in candidates)
        {
            if (target.Any(existing =>
                    string.Equals(existing.PackageId, candidate.PackageId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(existing.PackageVersion, candidate.PackageVersion, StringComparison.Ordinal)))
            {
                continue;
            }

            target.Add(candidate);
        }
    }

    private static bool SatisfiesConstraint(NuGetVersion version, VersionRange? constraint)
        => constraint is null || constraint.Satisfies(version);

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
        VersionRange? constraint)
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

        string rangeText = RangeText(constraint);
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

    private sealed record InstalledWorkerCandidate(ContentWorkloadInfo Workload, NuGetVersion Version);

    private sealed record ResolvedFunctionsWorker(
        FunctionsWorkerId Id,
        string WorkerRuntime,
        string WorkerConfigPath,
        string Version) : IFunctionsWorker;
}
