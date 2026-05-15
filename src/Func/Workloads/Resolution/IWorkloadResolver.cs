// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Decides which installed workload owns a given directory. Order:
/// explicit <c>--stack</c> selector, then registered
/// <see cref="IProjectResolver"/>s with <c>FUNCTIONS_WORKER_RUNTIME</c> in
/// <c>local.settings.json</c> as a tie-breaker.
/// </summary>
internal interface IWorkloadResolver
{
    /// <summary>
    /// Never throws on resolution failure; callers pattern-match the result.
    /// </summary>
    public Task<WorkloadResolution> ResolveAsync(WorkloadResolutionContext context, CancellationToken cancellationToken);
}
