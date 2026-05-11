// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Hands out the set of installed, hydrated workloads. The list is
/// materialized once at host startup by
/// <see cref="Hosting.WorkloadRegistration"/> and exposed through this
/// abstraction so consumers depend on a named domain concept rather than a
/// raw <see cref="IEnumerable{T}"/> of <see cref="WorkloadInfo"/>.
/// </summary>
internal interface IWorkloadProvider
{
    /// <summary>
    /// Returns the workloads that were loaded at boot. The result is stable
    /// for the lifetime of the host.
    /// </summary>
    public IReadOnlyList<WorkloadInfo> GetWorkloads();
}
