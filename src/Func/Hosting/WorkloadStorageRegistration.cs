// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Loading;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Wires the workload-storage subsystem (paths + manifest store) into DI.
/// Binds <see cref="WorkloadPathsOptions"/> from the <c>Workloads</c>
/// configuration section so the <c>FUNC_CLI_Workloads__Home</c> env var
/// (registered at host build) flows through, while tests can register their
/// own options without touching process-global state.
///
/// Also publishes the loaded-workloads catalog as a singleton so commands
/// (e.g. <c>func workload list</c>) read in-memory <see cref="WorkloadInfo"/>
/// instances rather than re-reading the registry on every invocation.
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

        // Materialize the installed-workloads list once, on first resolve.
        // The store + loader are still registered above so install / uninstall
        // commands can mutate / reload the registry; consumers that only need
        // to enumerate already-loaded workloads (list, alias routing, command
        // contribution) inject this singleton instead. Sync-over-async is
        // intentional: this runs at most once per process, before any command
        // executes, and there is no synchronization context to deadlock on
        // in a CLI.
        services.AddSingleton<IReadOnlyList<WorkloadInfo>>(sp =>
        {
            IWorkloadStore store = sp.GetRequiredService<IWorkloadStore>();
            IWorkloadLoader loader = sp.GetRequiredService<IWorkloadLoader>();
            IReadOnlyList<WorkloadEntry> entries = store.GetWorkloadsAsync().GetAwaiter().GetResult();
            return loader.Load(entries);
        });

        return services;
    }
}
