// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Templates.DotNet;

/// <summary>
/// DI registration for the DotNet engine. Hosted by <c>Func.csproj</c>;
/// callers reach the implementation only through this extension method so
/// the engine internals stay opaque.
/// </summary>
public static class DotNetServiceCollectionExtensions
{
    public static IServiceCollection AddDotNetTemplateEngine(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDotnetTemplateRunner, DefaultDotnetTemplateRunner>();
        services.AddSingleton<IItemTemplateHivePathProvider, ItemTemplateHivePathProvider>();
        services.AddSingleton<IItemTemplateHiveProvisioner, ItemTemplateHiveProvisioner>();
        services.AddSingleton<ITemplateEngineProvider, DotNetEngineProvider>();
        return services;
    }
}
