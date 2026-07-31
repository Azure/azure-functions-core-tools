// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Templates.Search;

/// <summary>
/// DI registrations for <c>func new --search</c>: the index consumer, the
/// term-matching search service, and the direct <c>--source</c> feed search.
/// Search overrides come from environment variables only (AGENTS.md forbids
/// app-style settings files), read once here into
/// <see cref="FuncTemplateSearchOptions"/>.
/// </summary>
internal static class TemplateSearchRegistration
{
    /// <summary>
    /// Named HttpClient for downloading the search index.
    /// </summary>
    internal const string HttpClientName = "TemplateSearchIndex";

    /// <summary>
    /// Overrides the search index location. Accepts an HTTPS URL, a
    /// <c>file://</c> URI, or an absolute local file path (e.g.
    /// <c>C:\index.json</c> or <c>/home/user/index.json</c>). A local file
    /// path resolves fully offline. Mirrors upstream
    /// <c>DOTNET_NEW_SEARCH_FILE_OVERRIDE</c>.
    /// </summary>
    internal const string IndexUriEnvVar = "FUNC_CLI_TEMPLATE_SEARCH_INDEX";

    /// <summary>
    /// When set to a truthy value, never downloads: uses only a previously
    /// cached index copy. Mirrors upstream
    /// <c>DOTNET_NEW_LOCAL_SEARCH_FILE_ONLY</c>.
    /// </summary>
    internal const string LocalOnlyEnvVar = "FUNC_CLI_TEMPLATE_SEARCH_LOCAL_ONLY";

    public static IServiceCollection AddTemplateSearch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<FuncTemplateSearchOptions>()
            .Configure(opts =>
            {
                string? indexOverride = Environment.GetEnvironmentVariable(IndexUriEnvVar);
                if (!string.IsNullOrWhiteSpace(indexOverride))
                {
                    opts.IndexUri = indexOverride;
                }

                if (IsTruthy(Environment.GetEnvironmentVariable(LocalOnlyEnvVar)))
                {
                    opts.LocalOnly = true;
                }
            });

        services.AddHttpClient(HttpClientName)
            .ConfigureHttpClient((sp, client) =>
            {
                FuncTemplateSearchOptions opts = sp.GetRequiredService<IOptions<FuncTemplateSearchOptions>>().Value;
                client.Timeout = opts.HttpTimeout;
            });

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IFuncSearchIndexProvider, FuncSearchIndexProvider>();
        services.AddSingleton<IFuncTemplateFeedSearch, FuncTemplateFeedSearch>();
        services.AddSingleton<IFuncTemplateSearchService, FuncTemplateSearchService>();

        return services;
    }

    private static bool IsTruthy(string? value)
        => !string.IsNullOrWhiteSpace(value)
        && (value.Equals("1", StringComparison.Ordinal)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase));
}
