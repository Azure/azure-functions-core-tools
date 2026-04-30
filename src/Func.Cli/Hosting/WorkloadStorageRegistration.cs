// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Wires the workload-storage subsystem (paths + manifest store) into DI.
/// Binds <see cref="WorkloadPathsOptions"/> from configuration so the
/// <c>FUNC_CLI_HOME</c> env var (added at host build) flows through, while
/// tests can register their own options without touching process-global state.
/// </summary>
internal static class WorkloadStorageRegistration
{
    public static IServiceCollection AddWorkloadStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<WorkloadPathsOptions>(configuration);
        services.AddSingleton<GlobalManifestStore>();

        return services;
    }
}
