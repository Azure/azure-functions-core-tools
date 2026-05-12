// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Registers the workload catalog services in DI.
/// </summary>
internal static class WorkloadCatalogRegistration
{
    /// <summary>
    /// Configuration section the catalog options bind from.
    /// </summary>
    public const string ConfigurationSection = "Workloads:Catalog";

    public static IServiceCollection AddWorkloadCatalog(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<WorkloadCatalogOptions>()
            .BindConfiguration(ConfigurationSection)
            .ValidateOnStart();

        services.AddSingleton<IPackageSourceProvider, PackageSourceProvider>();
        services.AddSingleton<Func<PackageSource, ISourceClient>>(_ => CreateSourceClient);
        services.AddSingleton<IWorkloadCatalog, WorkloadCatalog>();

        return services;
    }

    private static ISourceClient CreateSourceClient(PackageSource source)
    {
        if (source.IsLocal)
        {
            return new LocalFolderSourceClient(source);
        }

        SourceRepository repository = Repository.Factory.GetCoreV3(source);
        return new NuGetProtocolSourceClient(repository);
    }
}
