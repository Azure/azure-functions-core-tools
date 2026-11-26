// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Packages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Registers the independent template catalog using the shared NuGet source client factory.
/// </summary>
internal static class TemplateCatalogRegistration
{
    public static IServiceCollection AddTemplateCatalog(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<TemplateCatalogOptions>();
        services.AddSingleton<IConfigureOptions<TemplateCatalogOptions>, TemplateCatalogOptionsSetup>();
        services.AddSingleton<TemplateCatalog>();
        services.AddSingleton<TemplateSearch>();
        return services;
    }
}
