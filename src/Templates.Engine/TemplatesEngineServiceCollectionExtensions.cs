// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// DI registrations for the in-process Microsoft templating engine host.
/// Mirrors the per-subsystem extension methods used elsewhere
/// (<c>AddTemplatesOrchestrator</c>, <c>AddProfiles</c>, …). Registers the
/// load-bearing core (host, relocated-hive paths, session) plus the shared
/// seams; the acquisition and catalog subsystems add their implementations.
/// </summary>
internal static class TemplatesEngineServiceCollectionExtensions
{
    /// <summary>
    /// Registers the templating engine host, its relocated hive, and the
    /// process-lifetime engine session.
    /// </summary>
    public static IServiceCollection AddTemplatesEngine(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // TryAdd so the composition root can bridge the real running CLI
        // version (Func's ICliVersionProvider) over this assembly-stamped default.
        services.TryAddSingleton<IFuncTemplateEngineVersion, AssemblyFuncTemplateEngineVersion>();

        services.TryAddSingleton<IFuncExtensionBundleContextAccessor, FuncExtensionBundleContextAccessor>();

        services.AddSingleton<IPathInfo, FuncTemplateEnginePaths>();
        services.AddSingleton<ITemplateEngineHost, FuncTemplateEngineHost>();
        services.AddSingleton<IFuncTemplateEngineSession, FuncTemplateEngineSession>();

        services.AddSingleton<IFuncTemplateHiveLock, FuncTemplateHiveLock>();
        services.AddSingleton<IFuncTemplatePackageService, FuncTemplatePackageService>();

        // engine-catalog: catalog, scaffolder, and their shared seams.
        services.AddSingleton<IFuncTemplateCatalog, FuncTemplateCatalog>();
        services.AddSingleton<IFuncTemplateScaffolder, FuncTemplateScaffolder>();
        services.AddSingleton<IFuncTemplateMountFileReader, EngineTemplateMountFileReader>();
        services.AddSingleton<IFuncTemplateConstraintEvaluator, FuncTemplateConstraintEvaluator>();
        services.AddSingleton<IFuncTemplateFileSystem, PhysicalFuncTemplateFileSystem>();
        services.AddSingleton<IProjectFilePropertyReader, MsBuildProjectFilePropertyReader>();
        services.AddSingleton<IFuncProjectDirectoryAccessor, FuncProjectDirectoryAccessor>();
        services.AddSingleton<IFuncTemplateStagingArea, TempFuncTemplateStagingArea>();

        // Post-action dispatch: the allowlisted handlers keyed by ActionId.
        services.AddSingleton<IFuncPostActionDispatcher, FuncPostActionDispatcher>();
        services.AddSingleton<IFuncPostActionHandler, AppendPostActionHandler>();
        services.AddSingleton<IFuncPostActionHandler, AddReferencePostActionHandler>();
        services.AddSingleton<IFuncPostActionHandler, ManualInstructionsPostActionHandler>();

        // Custom engine components; DI-constructed so they capture the bundle /
        // project-directory accessors, then fixed into BuiltInComponents by the host.
        services.AddSingleton<ExtensionBundleConstraintFactory>();
        services.AddSingleton<MsBuildBindSymbolSource>();
        services.AddSingleton(sp => new FuncEngineComponent(
            typeof(Microsoft.TemplateEngine.Abstractions.Constraints.ITemplateConstraintFactory),
            sp.GetRequiredService<ExtensionBundleConstraintFactory>()));
        services.AddSingleton(sp => new FuncEngineComponent(
            typeof(Microsoft.TemplateEngine.Abstractions.Components.IBindSymbolSource),
            sp.GetRequiredService<MsBuildBindSymbolSource>()));

        return services;
    }
}
