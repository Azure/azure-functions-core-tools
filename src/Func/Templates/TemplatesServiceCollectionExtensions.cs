// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

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
    /// Registers the orchestrator services.
    /// </summary>
    public static IServiceCollection AddTemplatesOrchestrator(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<TemplateOptionHydrator>();
        services.AddSingleton<TemplatePicker>();
        services.AddSingleton<NewCommandRenderer>();
        services.AddSingleton<NewCommandRunner>();

        return services;
    }
}
