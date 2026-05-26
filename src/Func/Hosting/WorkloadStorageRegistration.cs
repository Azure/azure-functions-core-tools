// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Loading;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Wires the workload-storage subsystem (paths + manifest store + loader)
/// into DI. <see cref="WorkloadPathsOptions"/> is constructed directly (no
/// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> binding):
/// the only supported override for <c>Home</c> is the
/// <see cref="Constants.WorkloadsHomeEnvironmentVariable"/> env var, read
/// inside the options constructor via
/// <see cref="WorkloadHomeResolver.Resolve"/>. Integration tests substitute
/// a pre-built <see cref="WorkloadPathsOptions"/> singleton after
/// <see cref="CliHostFactory.CreateBuilder"/> to point the home elsewhere
/// without touching process-global state.
/// </summary>
internal static class WorkloadStorageRegistration
{
    public static IServiceCollection AddWorkloadStorage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<WorkloadPathsOptions>(_ => new WorkloadPathsOptions());
        services.AddSingleton<IWorkloadPaths>(sp => sp.GetRequiredService<WorkloadPathsOptions>());
        services.AddSingleton<IWorkloadStore, WorkloadStore>();
        services.AddSingleton<IWorkloadLoader, WorkloadLoader>();
        services.AddSingleton<IWorkloadMetadataReader, WorkloadMetadataReader>();

        return services;
    }
}
