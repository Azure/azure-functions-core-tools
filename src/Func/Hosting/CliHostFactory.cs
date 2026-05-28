// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Commands.Start.Azurite;
using Azure.Functions.Cli.Commands.Start.Host;
using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Hosting.FirstRun;
using Azure.Functions.Cli.Http;
using Azure.Functions.Cli.Quickstart;
using Azure.Functions.Cli.Telemetry;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Workers;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Configuration;
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
        var localSettingsProvider = new LocalSettingsProvider();
        var configurationPaths = new CliConfigurationPathsOptions();
        var configurationProvider = new CliConfigurationProvider(localSettingsProvider, configurationPaths);
        builder.Services.AddSingleton(localSettingsProvider);
        builder.Services.AddSingleton<ILocalSettingsProvider>(localSettingsProvider);
        builder.Services.AddSingleton(configurationPaths);
        builder.Services.AddSingleton(configurationProvider);
        builder.Services.AddSingleton<ICliConfigurationProvider>(configurationProvider);

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

        builder.Services.AddSingleton<IProcessEnvironment, ProcessEnvironment>();
        builder.Services.AddSingleton<IWorkerConfigFileSystem, WorkerConfigFileSystem>();
        builder.Services.AddSingleton<IFunctionsWorkerResolverFactory, DefaultFunctionsWorkerResolverFactory>();
        builder.Services.AddSingleton<IFunctionsProjectResolver, FunctionsProjectResolver>();
        builder.Services.AddSingleton<IInstalledBundleWorkloads, InstalledBundleWorkloads>();
        builder.Services.AddSingleton<InstalledBundleScanner>();
        builder.Services.AddSingleton<IHostJsonBundleSectionReader, HostJsonBundleSectionReader>();
        builder.Services.AddSingleton<IBundleResolveTelemetry>(NullBundleResolveTelemetry.Instance);
        builder.Services.AddSingleton<IExtensionBundleResolver, ExtensionBundleResolver>();
        builder.Services.AddSingleton<IHostWorkloadResolver, DefaultHostWorkloadResolver>();
        builder.Services.AddSingleton<HostProcessStartInfoFactory>();
        builder.Services.AddSingleton<IHostPortAvailability, TcpHostPortAvailability>();
        builder.Services.AddSingleton<IHostProcessFactory, DefaultHostProcessFactory>();
        builder.Services.AddSingleton<IHostProcessOutputParser, LineHostProcessOutputParser>();
        builder.Services.AddSingleton<IHostProcessRunner, DefaultHostProcessRunner>();
        builder.Services.AddSingleton<IStartInitializationRunner, DemoStartInitializationRunner>();

        builder.Services.AddSingleton<StartDashboardEventStreamFactory>();

        builder.Configuration.AddConfiguration(configurationProvider.GetEffectiveConfiguration(workingDirectory));
        builder.Services.AddSingleton<IConfigureOptions<StackOptions>, StackOptionsSetup>();
        builder.Services.AddSingleton<IConfigureOptions<HostStartupOptions>, HostStartupOptionsSetup>();

        builder.Services.AddCliHttpDefaults();
        builder.Services.AddBuiltInCommands();
        builder.Services.AddFirstRunExperience();
        builder.Services.AddQuickstartScaffolder();
        builder.Services.AddWorkloadStorage();
        builder.Services.AddProfiles();
        builder.Services.AddWorkloadCatalog();
        builder.Services.AddWorkloadInstaller();
        builder.Services.AddQuickstartManifest();
        builder.Services.AddManagedAzurite();
        builder.Services.AddTemplatesOrchestrator();

        return builder;
    }
}
