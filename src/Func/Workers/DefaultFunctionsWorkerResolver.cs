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
    IFunctionsWorkerContentResolver workerContentResolver,
    IReadOnlyDictionary<string, VersionRange>? activeWorkerConstraints = null) : IFunctionsWorkerResolver
{
    private readonly IWorkloadProvider _workloadProvider = workloadProvider ?? throw new ArgumentNullException(nameof(workloadProvider));
    private readonly IFunctionsWorkerContentResolver _workerContentResolver = workerContentResolver
        ?? throw new ArgumentNullException(nameof(workerContentResolver));
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
        return Task.FromResult(_workerContentResolver.ResolveWorker(workerId, installedWorkers, constraint, cancellationToken));
    }

    private IReadOnlyList<ContentWorkloadInfo> GetWorkerWorkloads(FunctionsWorkerId workerId)
    {
        string packageId = FunctionsWorkerWorkloadPackages.GetPackageId(workerId);
        string alias = GetWorkerAlias(workerId);
        List<ContentWorkloadInfo> workloads = [];
        AddDistinct(workloads, _workloadProvider.GetContentWorkloadsByPackageId(packageId));

        IEnumerable<ContentWorkloadInfo> candidates = _workloadProvider.GetContentWorkloads()
                .Where(workload => workload.Aliases.Any(candidate
                    => string.Equals(candidate, alias, StringComparison.OrdinalIgnoreCase)));

        AddDistinct(workloads, candidates);

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

    private static string GetWorkerAlias(FunctionsWorkerId workerId) => workerId.Value + "-worker";
}
