// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Default <see cref="IWorkloadProvider"/>. Snapshots the
/// <see cref="WorkloadInfo"/> singletons registered by
/// <see cref="Hosting.WorkloadRegistration"/> into a stable list.
/// </summary>
internal sealed class WorkloadProvider(IEnumerable<WorkloadInfo> workloads) : IWorkloadProvider
{
    private readonly IReadOnlyList<WorkloadInfo> _workloads =
        (workloads ?? throw new ArgumentNullException(nameof(workloads))).ToList();

    public IReadOnlyList<WorkloadInfo> GetWorkloads() => _workloads;
}
