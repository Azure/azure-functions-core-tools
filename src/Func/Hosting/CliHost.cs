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
/// Entry point for building the CLI's <see cref="HostApplicationBuilder"/>.
/// Wires interaction, telemetry, configuration, built-in commands, and
/// workload storage into the builder so callers can add their own
/// configuration before <see cref="HostApplicationBuilder.Build"/>.
/// </summary>
internal static class CliHost
{
    /// <summary>
    /// Creates a <see cref="HostApplicationBuilder"/> with all CLI-default
    /// services registered. Callers add any extra configuration, then call
    /// <see cref="HostApplicationBuilderExtensions.RegisterWorkloadsAsync"/>
    /// to load installed workloads, then <see cref="HostApplicationBuilder.Build"/>
    /// and <see cref="IHost.StartAsync"/>.
    /// </summary>
    /// <param name="interaction">Interaction service registered as a singleton.</param>
    public static HostApplicationBuilder CreateBuilder(IInteractionService interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        // Empty builder: skip the default config and logging providers a CLI
        // doesn't need. The host owns shared lifetimes (currently just the
        // OpenTelemetry pipeline) so flush + shutdown happen via host disposal.
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Services.AddSingleton(interaction);

        // Bridge the cli.workload.boot activity to the boot-duration histogram
        // so callers only need to start the activity. Idempotent.
        WorkloadBootMetricListener.EnsureRegistered();

        // Only wire OpenTelemetry when a connection string is available and the
        // user hasn't opted out; otherwise ActivitySource / Meter calls no-op.
        if (CliTelemetry.TryGetConnectionString(out string? connectionString))
        {
            builder.Services.AddOpenTelemetry()
                .ConfigureResource(r => CliTelemetry.ConfigureResource(r))
                .WithTracing(t => t
                    .AddSource(CliTelemetry.SourceName)
                    .AddAzureMonitorTraceExporter(o => o.ConnectionString = connectionString))
                .WithMetrics(m => m
                    .AddMeter(CliTelemetry.SourceName)
                    .AddAzureMonitorMetricExporter(o => o.ConnectionString = connectionString));
        }

        // FUNC_CLI_ prefix is stripped and "__" maps to section nesting, so
        // FUNC_CLI_Workloads__Home binds to WorkloadPathsOptions.Home.
        builder.Configuration.AddEnvironmentVariables(prefix: Constants.EnvironmentVariablePrefix);

        builder.Services.AddBuiltInCommands();
        builder.Services.AddWorkloadStorage();
        builder.Services.AddWorkloadInstaller();

        return builder;
    }
}
