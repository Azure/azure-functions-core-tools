// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Http;

/// <summary>
/// Configures CLI-wide HTTP client defaults (User-Agent, etc.) applied to all
/// named and unnamed <see cref="HttpClient"/> instances created via
/// <see cref="IHttpClientFactory"/>.
/// </summary>
internal static class HttpClientDefaults
{
    private static readonly string _userAgent = BuildUserAgent();

    public static IServiceCollection AddCliHttpDefaults(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.ConfigureHttpClientDefaults(builder =>
        {
            builder.ConfigureHttpClient(client =>
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _userAgent);
            });
        });

        return services;
    }

    private static string BuildUserAgent()
    {
        string version = typeof(HttpClientDefaults).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion?.Split('+')[0] ?? "unknown";

        string os = RuntimeInformation.OSDescription;
        return $"AzureFunctionsCli/{version} ({os})";
    }
}
