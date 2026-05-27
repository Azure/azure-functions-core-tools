// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// DI extension that registers quickstart manifest services.
/// </summary>
internal static class QuickstartRegistration
{
    /// <summary>
    /// Named HttpClient for manifest fetches.
    /// </summary>
    internal const string HttpClientName = "QuickstartManifest";

    /// <summary>
    /// Environment variable that overrides the CDN manifest URL.
    /// Accepts HTTPS URLs or local file paths.
    /// </summary>
    internal const string ManifestUrlEnvVar = "FUNC_QUICKSTART_MANIFEST_URL";

    public static IServiceCollection AddQuickstartManifest(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<QuickstartManifestOptions>()
            .Configure(opts =>
            {
                string? overrideUrl = Environment.GetEnvironmentVariable(ManifestUrlEnvVar);
                if (!string.IsNullOrWhiteSpace(overrideUrl))
                {
                    opts.ManifestUrl = overrideUrl;
                }
            });

        services.AddHttpClient(HttpClientName)
            .ConfigureHttpClient((sp, client) =>
            {
                QuickstartManifestOptions opts = sp.GetRequiredService<IOptions<QuickstartManifestOptions>>().Value;
                client.Timeout = opts.HttpTimeout;
            });

        services.AddSingleton<IManifestCache, ManifestCache>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IQuickstartManifestService, QuickstartManifestService>();

        return services;
    }
}
