// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Common.Processes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Update;

/// <summary>
/// Registers the Azure Functions CLI update pipeline.
/// </summary>
internal static class UpdateRegistration
{
    internal const string CdnBaseUrlEnvironmentVariable = "FUNC_CLI_UPDATE_CDN_URL";
    internal const string HttpClientName = "FuncCliUpdate";

    public static IServiceCollection AddCliUpdate(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<UpdateCdnOptions>()
            .Configure<IProcessEnvironment>((options, environment) =>
            {
                string? overrideUrl = environment.Get(CdnBaseUrlEnvironmentVariable);
                if (!string.IsNullOrWhiteSpace(overrideUrl))
                {
                    options.BaseUrl = overrideUrl;
                }
            });

        services.AddHttpClient(HttpClientName)
            .ConfigureHttpClient((serviceProvider, client) =>
            {
                UpdateCdnOptions options = serviceProvider.GetRequiredService<IOptions<UpdateCdnOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
                client.Timeout = options.HttpTimeout;
            });

        services.AddSingleton<IReleaseFeed>(serviceProvider =>
        {
            HttpClient client = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new CdnReleaseFeed(
                client,
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CdnReleaseFeed>>());
        });

        services.AddSingleton<ICliUpdater>(serviceProvider =>
        {
            HttpClient client = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            return new CliUpdater(
                client,
                serviceProvider.GetRequiredService<IFileSystem>(),
                serviceProvider.GetRequiredService<IOptions<CliEnvironmentOptions>>(),
                serviceProvider.GetRequiredService<IProcessRunner>(),
                serviceProvider.GetRequiredService<IUpdateLockProvider>(),
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CliUpdater>>());
        });

        services.TryAddSingleton<IFileSystem, PhysicalFileSystem>();
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.TryAddSingleton<IUpdateLockProvider, FileUpdateLockProvider>();

        return services;
    }
}
