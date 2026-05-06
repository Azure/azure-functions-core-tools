// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Bridges installed workloads into the host. In the final design this reads
/// the global manifest at <c>~/.azure-functions/workloads.json</c>, loads
/// each workload's entry-point assembly into its own
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/>, instantiates the
/// type identified by <c>[assembly: ExportCliWorkload&lt;T&gt;]</c>, and invokes
/// <see cref="Workload.Configure"/>.
///
/// At this stage <see cref="RegisterWorkloads"/> is a no-op: the loaded-
/// workloads list itself is published as a singleton by
/// <see cref="WorkloadStorageRegistration.AddWorkloadStorage"/>, so commands
/// that only need to enumerate installed workloads (e.g. <c>func workload
/// list</c>) inject <c>IReadOnlyList&lt;WorkloadInfo&gt;</c> directly. This
/// hook will grow to invoke <see cref="Workload.Configure"/> per loaded
/// workload once the contribution surface lands.
/// </summary>
internal static class WorkloadRegistration
{
    public static void RegisterWorkloads(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
    }
}
