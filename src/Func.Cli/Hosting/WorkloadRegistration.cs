// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Static seam where workloads are registered with the host. Empty in the
/// prototype — call <see cref="FuncCliHostBuilder.AddWorkload"/> here for any
/// in-repo workload you want bundled. The real implementation will replace
/// this with manifest-driven discovery from <c>~/.azure-functions/workloads/</c>
/// loaded via <c>AssemblyLoadContext</c>.
/// </summary>
internal static class WorkloadRegistration
{
    public static void RegisterKnownWorkloads(FuncCliHostBuilder builder)
    {
        // TODO(workload-loader): scan the install root for manifests and
        // load each workload assembly via AssemblyLoadContext.
        _ = builder;
    }
}
