// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Wires the install pipeline (<see cref="INuspecReader"/>,
/// <see cref="IWorkloadEntryPointScanner"/>, <see cref="IWorkloadInstaller"/>)
/// into DI. Depends on <see cref="WorkloadStorageRegistration.AddWorkloadStorage"/>
/// being registered first.
/// </summary>
internal static class WorkloadInstallRegistration
{
    public static IServiceCollection AddWorkloadInstaller(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<INuspecReader, NuspecReader>();
        services.AddSingleton<IWorkloadEntryPointScanner, WorkloadEntryPointScanner>();
        services.AddSingleton<IWorkloadInstaller, WorkloadInstaller>();

        return services;
    }
}
