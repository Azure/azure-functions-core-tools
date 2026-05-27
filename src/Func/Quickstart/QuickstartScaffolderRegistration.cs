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
    /// <remarks>
    /// TODO: When PR 2 (manifest service) merges, the shared HttpClient
    /// registration (user-agent, timeout, DI via AddCliHttpDefaults) will be
    /// available. Wire the named client through that path instead of bare
    /// IHttpClientFactory.
    /// </remarks>
    public static IServiceCollection AddQuickstartScaffolder(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(HttpTemplateFetcher.HttpClientName, client =>
        {
            // TODO: Use shared HttpClient registration with user-agent and other defaults once available. Post merge of PR 5109.
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
