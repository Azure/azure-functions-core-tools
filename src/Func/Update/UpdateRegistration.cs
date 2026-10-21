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
                    if (!Uri.TryCreate(overrideUrl, UriKind.Absolute, out Uri? parsed)
                        || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
                    {
                        throw new InvalidOperationException(
                            $"Environment variable '{CdnBaseUrlEnvironmentVariable}' must be an absolute HTTP(S) URL. Got: '{overrideUrl}'.");
                    }

                    if (!parsed.AbsolutePath.EndsWith('/'))
                    {
                        parsed = new Uri(parsed.GetLeftPart(UriPartial.Path) + "/" + parsed.Query + parsed.Fragment);
                    }

                    options.BaseUrl = parsed.AbsoluteUri;
                }
            });

        services.AddHttpClient(HttpClientName)
            .ConfigureHttpClient((serviceProvider, client) =>
            {
                UpdateCdnOptions options = serviceProvider.GetRequiredService<IOptions<UpdateCdnOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
                client.Timeout = options.HttpTimeout;
            });

        services.TryAddSingleton<IFileSystem, PhysicalFileSystem>();
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.TryAddSingleton<IUpdateLockProvider, FileUpdateLockProvider>();
        services.AddSingleton<IReleaseFeed, CdnReleaseFeed>();
        services.AddSingleton<ICliUpdater, CliUpdater>();

        return services;
    }
}
