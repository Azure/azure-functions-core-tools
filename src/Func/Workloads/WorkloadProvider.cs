// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Default <see cref="IWorkloadProvider"/>. Snapshots the workload inventory
/// registered by <see cref="Hosting.WorkloadRegistration"/> into stable lists.
/// </summary>
internal sealed class WorkloadProvider(IEnumerable<WorkloadInfo> workloads) : IWorkloadProvider
{
    private readonly Snapshot _snapshot = CreateSnapshot(workloads);

    public IReadOnlyList<WorkloadInfo> GetWorkloads() => _snapshot.Workloads;

    public IReadOnlyList<RuntimeWorkloadInfo> GetRuntimeWorkloads() => _snapshot.RuntimeWorkloads;

    public IReadOnlyList<ContentWorkloadInfo> GetContentWorkloads() => _snapshot.ContentWorkloads;

    private static Snapshot CreateSnapshot(IEnumerable<WorkloadInfo> workloads)
    {
        ArgumentNullException.ThrowIfNull(workloads);

        IReadOnlyList<WorkloadInfo> all = [.. workloads];
        return new Snapshot(
            all,
            [.. all.OfType<RuntimeWorkloadInfo>()],
            [.. all.OfType<ContentWorkloadInfo>()]);
    }

    private sealed record Snapshot(
        IReadOnlyList<WorkloadInfo> Workloads,
        IReadOnlyList<RuntimeWorkloadInfo> RuntimeWorkloads,
        IReadOnlyList<ContentWorkloadInfo> ContentWorkloads);
}
