// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Loading;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Wires the workload-storage subsystem (paths + manifest store + loader)
/// into DI. <see cref="WorkloadPathsOptions"/> is registered without binding
/// to <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>: the
/// only supported override for <c>Home</c> is the
/// <see cref="Constants.WorkloadsHomeEnvironmentVariable"/> env var, applied
/// via <see cref="WorkloadPathsOptionsSetup"/>. Tests substitute the
/// <see cref="Common.IEnvironmentVariables"/> singleton to point
/// the home elsewhere without touching process-global state.
/// </summary>
internal static class WorkloadStorageRegistration
{
    public static IServiceCollection AddWorkloadStorage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<WorkloadPathsOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IConfigureOptions<WorkloadPathsOptions>, WorkloadPathsOptionsSetup>();

        // WorkloadPathsOptions implements IWorkloadPaths directly. Resolve the
        // single bound options instance so consumers don't have to unwrap
        // IOptions<> themselves.
        services.AddSingleton<IWorkloadPaths>(
            sp => sp.GetRequiredService<IOptions<WorkloadPathsOptions>>().Value);
        services.AddSingleton<IWorkloadStore, WorkloadStore>();
        services.AddSingleton<IWorkloadLoader, WorkloadLoader>();
        services.AddSingleton<IWorkloadMetadataReader, WorkloadMetadataReader>();

        return services;
    }
}
