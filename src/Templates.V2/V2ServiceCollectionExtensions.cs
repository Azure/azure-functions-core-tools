// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Templates.V2;

/// <summary>
/// DI registration for the V2 engine. Hosted by <c>Func.csproj</c>; surfaces
/// a single extension method so the engine implementation stays opaque from
/// the host's perspective.
/// </summary>
public static class V2ServiceCollectionExtensions
{
    public static IServiceCollection AddV2TemplateEngine(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ITemplateEngineProvider, V2EngineProvider>();
        return services;
    }
}
