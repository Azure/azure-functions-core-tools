// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Bridges installed workloads into the host. In the final design this reads
/// the global manifest at <c>~/.azure-functions/workloads.json</c>, loads
/// each workload's entry-point assembly into its own
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/>, instantiates the
/// type named in the per-package <c>workload.json</c>, and invokes
/// <see cref="IWorkload.Configure"/>.
///
/// At this stage the loader/installer haven't landed yet.
/// <see cref="RegisterWorkloads"/> registers an empty
/// <see cref="InstalledWorkload"/> list so DI consumers (e.g.
/// <c>func workload list</c>) get a deterministic empty result; the real
/// loader will replace this in a follow-up PR.
/// </summary>
internal static class WorkloadRegistration
{
    public static void RegisterWorkloads(IFunctionsCliBuilder builder)
    {
        builder.Services.AddSingleton<IReadOnlyList<InstalledWorkload>>(Array.Empty<InstalledWorkload>());
    }
}
