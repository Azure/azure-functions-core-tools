// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Hands out the currently-installed, hydrated workloads. Composes the
/// registry (<see cref="Storage.IWorkloadStore"/>) with the assembly loader
/// (<see cref="Loading.IWorkloadLoader"/>) and caches the result so repeated
/// command invocations don't re-read <c>workloads.json</c> or re-activate
/// already-loaded assemblies.
/// </summary>
internal interface IWorkloadProvider
{
    /// <summary>
    /// Returns the loaded workloads, materializing them on first access and
    /// returning the cached list on subsequent calls. Safe to call
    /// concurrently; loading happens at most once per provider instance.
    /// </summary>
    public ValueTask<IReadOnlyList<WorkloadInfo>> GetWorkloadsAsync(
        CancellationToken cancellationToken = default);
}
