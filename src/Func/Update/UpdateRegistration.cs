// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite.Processes;
using Azure.Functions.Cli.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Update;

/// <summary>
/// DI wiring for the <c>func update</c> pipeline: release feed, updater,
/// install-method detector, and their shared infrastructure (typed
/// HttpClients, filesystem seam, process runner).
/// </summary>
internal static class UpdateRegistration
{
    /// <summary>
    /// Environment variable that overrides the CDN base URL. Accepts any
    /// absolute <c>http(s)</c> URI ending in <c>/</c>. Used by end-to-end
    /// tests and pre-release feeds; production ignores it when unset.
    /// </summary>
    internal const string CdnBaseUrlEnvVar = "FUNC_CLI_UPDATE_CDN_URL";

    /// <summary>
    /// Named HttpClient shared by the release feed and updater. Named (rather
    /// than typed) so both consumers get the same <see cref="HttpClient.BaseAddress"/>
    /// wiring without duplicating configuration.
    /// </summary>
    internal const string HttpClientName = "FuncCliUpdate";

    public static IServiceCollection AddCliUpdate(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<UpdateCdnOptions>()
            .Configure(opts =>
            {
                string? overrideUrl = Environment.GetEnvironmentVariable(CdnBaseUrlEnvVar);
                if (!string.IsNullOrWhiteSpace(overrideUrl))
                {
                    opts.BaseUrl = overrideUrl;
                }
            });

        services.AddHttpClient(HttpClientName)
            .ConfigureHttpClient((sp, client) =>
            {
                UpdateCdnOptions opts = sp.GetRequiredService<IOptions<UpdateCdnOptions>>().Value;
                client.BaseAddress = new Uri(opts.BaseUrl, UriKind.Absolute);
                client.Timeout = opts.HttpTimeout;
            });

        services.AddSingleton<IReleaseFeed>(sp =>
        {
            HttpClient client = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new CdnReleaseFeed(client, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CdnReleaseFeed>>());
        });

        services.AddSingleton<ICliUpdater>(sp =>
        {
            HttpClient client = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new CliUpdater(
                client,
                sp.GetRequiredService<IUpdateFileSystem>(),
                sp.GetRequiredService<ICliEnvironment>(),
                sp.GetRequiredService<IProcessRunner>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CliUpdater>>());
        });

        services.TryAddSingleton<IUpdateFileSystem, UpdateFileSystem>();
        services.TryAddSingleton<ICliEnvironment, CliEnvironment>();
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.TryAddSingleton<IInstallMethodDetector, InstallMethodDetector>();

        return services;
    }
}
