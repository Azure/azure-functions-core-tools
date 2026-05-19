// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Decides which installed workload owns a given directory. Order:
/// explicit <c>--stack</c> selector, then a <c>host.json</c> gate that
/// rejects directories which don't look like Functions projects, then the
/// configured worker runtime filtering registered <see cref="IProjectResolver"/>
/// claims, then unfiltered <see cref="IProjectResolver"/> auto-detection as a fallback.
/// </summary>
internal interface IWorkloadResolver
{
    /// <summary>
    /// Never throws on resolution failure; callers pattern-match the result.
    /// </summary>
    public Task<WorkloadResolution> ResolveAsync(WorkloadResolutionContext context, CancellationToken cancellationToken);
}
