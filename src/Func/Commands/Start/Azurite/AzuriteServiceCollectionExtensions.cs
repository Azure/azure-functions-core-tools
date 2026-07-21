// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite.Launching;
using Azure.Functions.Cli.Commands.Start.Azurite.Orchestration;
using Azure.Functions.Cli.Commands.Start.Azurite.Processes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// DI registration helpers for the managed-Azurite feature.
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
        })
        .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
        {
            // Azurite always runs on the loopback interface, so a system or
            // environment proxy (HTTP_PROXY, WinHTTP, WPAD) must never be in
            // the path. Without this, a configured proxy intercepts the probe
            // and returns Connection refused / timeout, the probe reports
            // NotListening, and the orchestrator launches a second Azurite
            // that fails to bind the already-occupied ports (issue #5200).
            UseProxy = false,
            AllowAutoRedirect = false,
        });

        services.AddSingleton<IAzuriteProbe, AzuriteProbe>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="IClock"/> and <see cref="IAzuriteManagedPathsProvider"/>
    /// for resolving managed-Azurite on-disk locations. The path provider
    /// reads <c>&lt;funcHome&gt;</c> from <see cref="Configuration.CliConfigurationPathsOptions"/>,
    /// which is registered separately by the CLI host.
    /// </summary>
    public static IServiceCollection AddAzuriteManagedPaths(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IAzuriteManagedPathsProvider, AzuriteManagedPathsProvider>();

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

        services.TryAddSingleton<IAzuriteHostEnvironment, AzuriteHostEnvironment>();
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.TryAddSingleton<IAzuriteExecutableLocator, AzuriteExecutableLocator>();
        services.TryAddSingleton<IDockerAvailabilityProbe, DockerAvailabilityProbe>();
        services.TryAddSingleton<IPortOwnershipStrategy>(
            static _ => OperatingSystem.IsWindows() ? new WindowsPortOwnershipStrategy() : new UnixPortOwnershipStrategy());
        services.TryAddSingleton<IListeningProcessInspector, ListeningProcessInspector>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="IAzuriteLauncher"/>. Kept separate from
    /// <see cref="AddAzuriteDiscovery"/> so tests can swap the launcher
    /// without losing the discovery helpers.
    /// </summary>
    public static IServiceCollection AddAzuriteLauncher(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IAzuriteLauncher, AzuriteLauncher>();
        return services;
    }

    /// <summary>
    /// Registers everything needed for the managed-Azurite feature: the
    /// classifier, probe, discovery seams, launcher, paths, and the
    /// orchestrator that wires them together. Idempotent: each call uses
    /// <c>TryAdd</c> semantics so it is safe to invoke from both the
    /// composition root and individual tests.
    /// </summary>
    public static IServiceCollection AddManagedAzurite(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAzuriteProbe();
        services.AddAzuriteDiscovery();
        services.AddAzuriteLauncher();
        services.AddAzuriteManagedPaths();

        services.TryAddSingleton<IAzureWebJobsStorageClassifier, AzureWebJobsStorageClassifier>();
        services.TryAddSingleton<IManagedAzuriteOrchestrator, ManagedAzuriteOrchestrator>();

        return services;
    }
}
