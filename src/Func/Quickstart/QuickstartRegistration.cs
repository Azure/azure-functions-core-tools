// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Quickstart;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// DI extension that registers all quickstart-related services.
/// </summary>
internal static class QuickstartRegistration
{
    /// <summary>
    /// When set, overrides <see cref="QuickstartManifestOptions.ManifestUrl"/> with the
    /// given URL or local <c>file://</c> path. Used for staging CDN validation and local
    /// manifest authoring against an unpublished manifest.
    /// </summary>
    internal const string ManifestUrlEnvVar = "FUNC_TEMPLATE_MANIFEST_URL";

    public static IServiceCollection AddQuickstart(this IServiceCollection services)
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

        // Named HttpClient so each service gets its own instance and lifecycle.
        services.AddHttpClient<IQuickstartManifestClient, QuickstartManifestClient>(
            nameof(QuickstartManifestClient));

        services.AddSingleton<IGitRunner, GitRunner>();
        services.AddHttpClient<IQuickstartScaffolder, QuickstartScaffolder>(
            nameof(QuickstartScaffolder));

        services.AddSingleton<QuickstartListCommand>();
        services.AddSingleton<QuickstartInfoCommand>();

        return services;
    }
}
