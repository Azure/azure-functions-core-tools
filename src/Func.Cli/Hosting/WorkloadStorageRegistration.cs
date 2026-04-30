// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Wires the workload-storage subsystem (paths + manifest store) into DI.
/// Binds <see cref="WorkloadPathsOptions"/> from the <c>Workloads</c>
/// configuration section so the <c>FUNC_CLI_Workloads__Home</c> env var
/// (added at host build) flows through, while tests can register their own
/// options without touching process-global state.
/// </summary>
internal static class WorkloadStorageRegistration
{
    /// <summary>
    /// Configuration section name workload paths bind from.
    /// </summary>
    public const string ConfigurationSection = "Workloads";

    public static IServiceCollection AddWorkloadStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<WorkloadPathsOptions>()
            .Bind(configuration.GetSection(ConfigurationSection))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IWorkloadPaths, WorkloadPaths>();
        services.AddSingleton<IGlobalManifestStore, GlobalManifestStore>();

        return services;
    }
}
