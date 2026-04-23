// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workload.Dotnet;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Static seam where workloads are registered with the host. In this prototype
/// the set of known workloads is hard-coded; the real implementation will
/// replace this with discovery from <c>~/.azure-functions/workloads/</c> via
/// <c>AssemblyLoadContext</c> per the workload spec.
/// </summary>
internal static class WorkloadRegistration
{
    public static void RegisterKnownWorkloads(FuncCliHostBuilder builder)
    {
        // TODO(workload-loader): replace static list with manifest-driven discovery.
        builder.AddWorkload(new DotnetWorkload());
    }
}
