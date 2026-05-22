// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Hands out the workload inventory materialized once at host startup by
/// <see cref="Hosting.WorkloadRegistration"/>.
/// </summary>
internal interface IWorkloadProvider
{
    /// <summary>
    /// Returns runtime and content workloads discovered at boot. The result
    /// is stable for the lifetime of the host.
    /// </summary>
    public IReadOnlyList<WorkloadInfo> GetWorkloads();

    /// <summary>
    /// Returns runtime workloads that were loaded and configured at boot.
    /// </summary>
    public IReadOnlyList<RuntimeWorkloadInfo> GetRuntimeWorkloads();

    /// <summary>
    /// Returns installed content workloads discovered at boot.
    /// </summary>
    public IReadOnlyList<ContentWorkloadInfo> GetContentWorkloads();
}
