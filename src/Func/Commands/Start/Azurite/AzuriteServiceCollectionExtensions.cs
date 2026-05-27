// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// DI registration helpers for the managed-Azurite feature. Not wired into the
/// CLI host yet; later slices will call this from the composition root.
/// </summary>
internal static class AzuriteServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IAzuriteProbe"/> and its named <see cref="HttpClient"/>.
    /// </summary>
    public static IServiceCollection AddAzuriteProbe(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(AzuriteProbe.HttpClientName, static client =>
        {
            // Cover connect + send + receive headers for a single probe. The
            // probe itself also enforces a per-request timeout via a linked
            // CancellationTokenSource so callers can preempt sooner.
            client.Timeout = AzuriteProbe.PerRequestTimeout;
        });

        services.AddSingleton<IAzuriteProbe, AzuriteProbe>();
        return services;
    }
}
