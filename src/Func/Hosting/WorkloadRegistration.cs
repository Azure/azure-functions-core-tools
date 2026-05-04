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
/// <see cref="IWorkload.Configure"/>.
///
/// At this stage the loader hasn't landed yet, so <see cref="RegisterWorkloads"/>
/// is a no-op. Commands that only need to enumerate installed workloads
/// (e.g. <c>func workload list</c>) read the global manifest directly via
/// <see cref="Workloads.Storage.IGlobalManifestStore"/>; they don't need the
/// loader at all.
/// </summary>
internal static class WorkloadRegistration
{
    public static void RegisterWorkloads(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
    }
}
