// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Install;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Wires the install pipeline (<see cref="IWorkloadInstaller"/>) into DI.
/// Requires <see cref="WorkloadStorageRegistration.AddWorkloadStorage"/> to
/// be registered first.
/// </summary>
internal static class WorkloadInstallRegistration
{
    public static IServiceCollection AddWorkloadInstaller(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IWorkloadInstaller, WorkloadInstaller>();

        return services;
    }
}
