// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Telemetry;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Builds the CLI host with all services, configuration, and workloads wired in.
/// </summary>
internal static class CliHostFactory
{
    /// <summary>
    /// Creates and starts the CLI host. Registers built-in commands and
    /// workload storage, loads installed workloads, and invokes each
    /// workload's <see cref="Workloads.Workload.Configure"/>. The returned
    /// host is already started so OpenTelemetry listeners are subscribed and
    /// hosted services are running before any command code executes.
    /// </summary>
    /// <param name="interaction">The interaction service used for warnings, errors, and prompts. Registered as a singleton in the host.</param>
    /// <param name="configureConfiguration">Optional hook for tests to override configuration sources (e.g. point <c>Workloads:Home</c> at a temp directory).</param>
    /// <param name="cancellationToken">Cancellation propagated to <see cref="IHost.StartAsync"/> and the workload registration step.</param>
    public static async Task<IHost> CreateHostAsync(
        IInteractionService interaction,
        Action<IConfigurationBuilder>? configureConfiguration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        // Empty builder: skip the default config and logging providers a CLI
        // doesn't need. The host owns shared lifetimes (currently just the
        // OpenTelemetry pipeline) so flush + shutdown happen via host disposal.
        HostApplicationBuilder hostBuilder = Host.CreateEmptyApplicationBuilder(null);
        hostBuilder.Services.AddSingleton(interaction);

        // Only wire OpenTelemetry when a connection string is available and the
        // user hasn't opted out; otherwise ActivitySource / Meter calls no-op.
        if (CliTelemetry.TryGetConnectionString(out string? connectionString))
        {
            hostBuilder.Services.AddOpenTelemetry()
                .ConfigureResource(r => CliTelemetry.ConfigureResource(r))
                .WithTracing(t => t
                    .AddSource(CliTelemetry.SourceName)
                    .AddAzureMonitorTraceExporter(o => o.ConnectionString = connectionString))
                .WithMetrics(m => m
                    .AddMeter(CliTelemetry.SourceName)
                    .AddAzureMonitorMetricExporter(o => o.ConnectionString = connectionString));
        }

        hostBuilder.Services.AddBuiltInCommands();

        // FUNC_CLI_ prefix is stripped and "__" maps to section nesting, so
        // FUNC_CLI_Workloads__Home binds to WorkloadPathsOptions.Home.
        hostBuilder.Configuration.AddEnvironmentVariables(prefix: Constants.EnvironmentVariablePrefix);
        configureConfiguration?.Invoke(hostBuilder.Configuration);
        hostBuilder.Services.AddWorkloadStorage();

        // Runs before Build() so workloads can still mutate IServiceCollection.
        // The boot duration is recorded after StartAsync below — OTel listeners
        // aren't subscribed until the host starts, so emitting earlier drops it.
        WorkloadRegistrationResult registration = await WorkloadRegistration.RegisterWorkloadsAsync(
            hostBuilder.Services,
            hostBuilder.Configuration,
            interaction,
            cancellationToken);

        IHost host = hostBuilder.Build();

        // Start before any command code emits telemetry so OTel listeners are
        // subscribed.
        await host.StartAsync(cancellationToken);

        CliTelemetry.Metric.RecordWorkloadBoot(
            registration.WorkloadCount,
            registration.ElapsedMilliseconds);

        return host;
    }
}
