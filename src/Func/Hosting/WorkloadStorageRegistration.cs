// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

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
/// <see cref="WorkloadPathsOptions.HomeEnvironmentVariable"/> env var,
/// applied via the options default. Tests that need a different home call
/// <see cref="OptionsServiceCollectionExtensions.Configure{TOptions}(IServiceCollection, Action{TOptions})"/>
/// directly.
/// </summary>
internal static class WorkloadStorageRegistration
{
    public static IServiceCollection AddWorkloadStorage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<WorkloadPathsOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

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
