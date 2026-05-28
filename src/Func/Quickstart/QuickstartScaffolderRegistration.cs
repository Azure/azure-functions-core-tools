// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Registers quickstart scaffolding services with DI.
/// </summary>
internal static class QuickstartScaffolderRegistration
{
    /// <summary>
    /// Adds the quickstart scaffolder and its dependencies (fetchers, git runner).
    /// </summary>
    public static IServiceCollection AddQuickstartScaffolder(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(HttpTemplateFetcher.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        services.AddSingleton<IGitRunner, GitRunner>();
        services.AddSingleton<IFetchModeResolver, FetchModeResolver>();
        services.AddSingleton<ITemplateFetcher, GitTemplateFetcher>();
        services.AddSingleton<ITemplateFetcher, HttpTemplateFetcher>();
        services.AddSingleton<IQuickstartScaffolder, QuickstartScaffolder>();

        return services;
    }
}
