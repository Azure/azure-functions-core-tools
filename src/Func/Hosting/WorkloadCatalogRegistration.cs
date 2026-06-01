// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Registers the workload catalog services in DI.
/// </summary>
internal static class WorkloadCatalogRegistration
{
    public static IServiceCollection AddWorkloadCatalog(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<WorkloadCatalogOptions>();
        services.AddSingleton<IConfigureOptions<WorkloadCatalogOptions>, WorkloadCatalogOptionsSetup>();

        services.AddSingleton<IPackageSourceProvider, PackageSourceProvider>();
        services.AddSingleton<Func<PackageSource, NuGetProtocolSourceClient>>(_ => CreateSourceClient);
        services.AddSingleton<IWorkloadCatalog, WorkloadCatalog>();

        return services;
    }

    private static NuGetProtocolSourceClient CreateSourceClient(PackageSource source)
    {
        // GetCoreV3 dispatches based on the source URI: HTTPS service indexes get the
        // v3 protocol stack; file:// roots get NuGet's local-feed reader. The catalog
        // code stays uniform either way.
        SourceRepository repository = Repository.Factory.GetCoreV3(source);
        return new NuGetProtocolSourceClient(repository);
    }
}
