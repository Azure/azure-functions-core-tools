// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.DependencyInjection;

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

        services.AddSingleton<IFuncTemplateEngineHostFactory, FuncTemplateEngineHostFactory>();
        services.AddSingleton<IInstalledTemplatesWorkloads, InstalledTemplatesWorkloads>();
        services.AddSingleton<ITemplateEngineProviderRegistry, TemplateEngineProviderRegistry>();
        services.AddSingleton<TemplateOptionHydrator>();
        services.AddSingleton<TemplatePicker>();
        services.AddSingleton<NewCommandRenderer>();
        services.AddSingleton<NewCommandRunner>();

        return services;
    }
}
