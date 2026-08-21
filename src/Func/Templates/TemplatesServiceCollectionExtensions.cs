// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// DI registrations for the <c>func new</c> / <c>func new --list</c>
/// orchestrator. Mirrors the per-subsystem extension methods used elsewhere
/// (<c>AddQuickstartScaffolder</c>, <c>AddProfiles</c>, …).
/// </summary>
internal static class TemplatesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the orchestrator services. Engine providers register from
    /// their own CLI-internal csprojs (<c>Templates.V2</c>, <c>Templates.DotNet</c>)
    /// via their own extension methods.
    /// </summary>
    public static IServiceCollection AddTemplatesOrchestrator(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IInstalledTemplatesWorkloads, InstalledTemplatesWorkloads>();
        services.AddSingleton<ITemplateEngineProviderRegistry, TemplateEngineProviderRegistry>();
        services.AddSingleton<INewCommandContextResolver, NewCommandContextResolver>();
        services.AddSingleton<ITemplatesWorkloadManifestReader, TemplatesWorkloadManifestReader>();
        services.AddSingleton<INewCommandBundleValidator, NewCommandBundleValidator>();
        services.AddSingleton<TemplateOptionHydrator>();
        services.AddSingleton<INewCommandTemplateCatalog, NewCommandTemplateCatalog>();
        services.AddSingleton<INewCommandTemplateApplicator, NewCommandTemplateApplicator>();
        services.AddSingleton<INewCommandTemplateOptionProvider, NewCommandTemplateOptionProvider>();
        services.AddSingleton<TemplatePicker>();
        services.AddSingleton<INewCommandTemplateSelector, NewCommandTemplateSelector>();
        services.AddSingleton<NewCommandRenderer>();
        services.AddSingleton<INewCommandResultRenderer, NewCommandResultRenderer>();
        services.AddSingleton<INewCommandRunner, NewCommandRunner>();

        return services;
    }
}
