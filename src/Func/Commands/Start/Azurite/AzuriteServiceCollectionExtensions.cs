// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite.Processes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    /// <summary>
    /// Registers the host-side seams and discovery services used by the CLI
    /// to locate Azurite and probe for Docker. Does not start anything and
    /// does not depend on probe/process startup services from later slices.
    /// </summary>
    public static IServiceCollection AddAzuriteDiscovery(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IPlatform, Platform>();
        services.TryAddSingleton<IAzuriteHostEnvironment, AzuriteHostEnvironment>();
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.TryAddSingleton<IAzuriteExecutableLocator, AzuriteExecutableLocator>();
        services.TryAddSingleton<IDockerAvailabilityProbe, DockerAvailabilityProbe>();

        return services;
    }
}
