// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Telemetry;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Builds and starts the CLI's <see cref="IHost"/>. Encapsulates the boot
/// sequence (create builder, register installed workloads, build, start) so
/// callers don't repeat it. Tests that need to intercept builder
/// configuration use <see cref="CreateBuilder"/> + the
/// <see cref="HostApplicationBuilderExtensions.RegisterWorkloadsAsync"/>
/// extension directly.
/// </summary>
internal static class CliHostFactory
{
    /// <summary>
    /// Creates the builder, registers installed workloads, and builds the
    /// host. Caller is responsible for <see cref="IHost.StartAsync"/>.
    /// </summary>
    public static async Task<IHost> CreateHostAsync(IInteractionService interaction, CancellationToken cancellationToken = default)
    {
        HostApplicationBuilder builder = CreateBuilder(interaction);
        await builder.RegisterWorkloadsAsync(cancellationToken);
        return builder.Build();
    }

    /// <summary>
    /// Creates a <see cref="HostApplicationBuilder"/> with all CLI-default
    /// services registered. Exposed for tests that need to add configuration
    /// before workloads register; production code should use
    /// <see cref="CreateHostAsync"/>.
    /// </summary>
    public static HostApplicationBuilder CreateBuilder(IInteractionService interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        // Empty builder: skip the default config and logging providers a CLI
        // doesn't need. The host owns shared lifetimes (currently just the
        // OpenTelemetry pipeline) so flush + shutdown happen via host disposal.
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Services.AddSingleton(interaction);

        var workingDirectory = new DirectoryInfo(Environment.CurrentDirectory);
        ILocalSettingsProvider localSettingsProvider = new LocalSettingsProvider();
        var configurationSourceBuilder = new CliConfigurationSourceBuilder(localSettingsProvider);
        builder.Services.AddSingleton(localSettingsProvider);
        builder.Services.AddSingleton(configurationSourceBuilder);

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

        // FUNC_CLI_ prefix is stripped and "__" maps to section nesting.
        // Note: WorkloadPathsOptions.Home is intentionally NOT bound from here;
        // it is sourced exclusively from the FUNC_CLI_Workloads__Home env var
        // (see WorkloadPathsOptions) so the workload root cannot be redirected
        // by host.json, local.settings.json, or any other config layer.
        configurationSourceBuilder.AddSources(builder.Configuration, workingDirectory);
        builder.Services.AddSingleton<IConfigureOptions<StackOptions>, StackOptionsSetup>();
        builder.Services.AddSingleton<IConfigureOptions<HostStartupOptions>, HostStartupOptionsSetup>();

        builder.Services.AddBuiltInCommands();
        builder.Services.AddWorkloadStorage();
        builder.Services.AddWorkloadCatalog();
        builder.Services.AddWorkloadInstaller();

        return builder;
    }
}
