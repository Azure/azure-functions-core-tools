// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Composes staged native setup without enabling it in the default CLI host.
/// </summary>
internal static class NativeTemplateSetupRegistration
{
    public static IServiceCollection AddNativeTemplateSetup(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<BasicCompanionManifestReader>();
        services.AddSingleton<IBasicCompanionCatalog, BundledBasicCompanionCatalog>();
        services.AddSingleton<BasicAcquisitionPlanner>();
        services.AddSingleton<IBasicPackagePayloadSource, BasicPackagePayloadSource>();
        services.AddSingleton<INativeTemplateOwnership, NativeTemplateOwnership>();
        services.AddSingleton<IBasicTemplateOwnership>(provider => provider.GetRequiredService<INativeTemplateOwnership>());
        services.AddSingleton<IBasicTemplateLifecycle, BasicTemplateLifecycle>();
        services.AddSingleton<BasicAcquisitionRunner>();
        services.AddSingleton<NativeTemplateSetupSupport>(provider =>
            new(provider.GetRequiredService<BasicAcquisitionRunner>(), AssemblyCliVersionProvider.Instance));
        return services;
    }

    /// <summary>
    /// Adds internal artifact binding without activating a public init or acquisition route.
    /// </summary>
    public static IServiceCollection AddNativeTemplateArtifactBinding(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<NativeTemplateArtifactVerifier>();
        services.AddSingleton<IBasicArchiveInspector, BasicArtifactInspector>();
        services.AddSingleton<IBoundInitScopeFactory, BoundInitScopeFactory>();
        services.AddSingleton<Quickstart.QuickstartInstalledTemplates>();
        return services;
    }
}
