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
/// into DI. Binds <see cref="WorkloadPathsOptions"/> from the
/// <c>Workloads</c> configuration section so the
/// <c>FUNC_CLI_Workloads__Home</c> env var (registered at host build) flows
/// through, while tests can register their own options without touching
/// process-global state.
/// </summary>
internal static class WorkloadStorageRegistration
{
    /// <summary>
    /// Configuration section name workload paths bind from.
    /// </summary>
    public const string ConfigurationSection = "Workloads";

    public static IServiceCollection AddWorkloadStorage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<WorkloadPathsOptions>()
            .BindConfiguration(ConfigurationSection)
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
