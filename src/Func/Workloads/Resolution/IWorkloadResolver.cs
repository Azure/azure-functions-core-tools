// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Decides which installed workload owns a given directory. Implements the
/// resolution algorithm specified in the workload spec §5.2:
/// explicit <c>--stack</c> selector → <c>FUNCTIONS_WORKER_RUNTIME</c> in
/// <c>local.settings.json</c> → registered <see cref="IProjectDetector"/>s
/// (with project-marker pre-filter and tie-breaking).
/// </summary>
internal interface IWorkloadResolver
{
    /// <summary>
    /// Returns a <see cref="WorkloadResolution"/> describing whether a single
    /// workload owns the directory, multiple workloads claim it, or none do.
    /// Never throws on resolution failure; callers inspect
    /// <see cref="WorkloadResolution.Status"/> instead.
    /// </summary>
    public Task<WorkloadResolution> ResolveAsync(WorkloadResolutionContext context, CancellationToken cancellationToken);
}
