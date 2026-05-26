// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Quickstart;
using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Quickstart;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Loading;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Tests;

/// <summary>
/// Test helper that builds the same DI shape Program.cs builds at runtime,
/// built-in commands plus an empty workload list, so tests can call
/// <see cref="Parser.CreateCommand"/> without standing up a host.
/// </summary>
internal static class TestParser
{
    public static FuncRootCommand CreateRoot(IInteractionService interaction)
    {
        ServiceCollection services = BuildBaseServices(interaction);
        return Parser.CreateCommand(services.BuildServiceProvider());
    }

    /// <summary>
    /// Builds a root command tree with a workload-scoped
    /// <see cref="DefaultFunctionsCliBuilder"/> available to <paramref name="configure"/>.
    /// Lets tests exercise <see cref="FunctionsCliBuilder.RegisterCommand(FuncCommand)"/>
    /// without booting the (not-yet-implemented) workload loader.
    /// </summary>
    public static FuncRootCommand CreateRootWithWorkload(
        IInteractionService interaction,
        RuntimeWorkloadInfo workload,
        Action<FunctionsCliBuilder> configure)
    {
        ServiceCollection services = BuildBaseServices(interaction);
        var builder = new DefaultFunctionsCliBuilder(services, workload);
        configure(builder);
        return Parser.CreateCommand(services.BuildServiceProvider());
    }

    /// <summary>
    /// Builds an <see cref="IServiceProvider"/> wired with all built-in commands
    /// plus any extra registrations applied by <paramref name="configure"/>.
    /// Exposed for tests that need to resolve <see cref="FuncCliCommand"/> services
    /// directly without going through <see cref="Parser.CreateCommand"/>.
    /// </summary>
    public static IServiceProvider BuildServiceProviderWith(
        IInteractionService interaction,
        Action<IServiceCollection>? configure = null)
    {
        ServiceCollection services = BuildBaseServices(interaction);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static ServiceCollection BuildBaseServices(IInteractionService interaction)
    {
        var services = new ServiceCollection();
        services.AddSingleton(interaction);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddOptions<StackOptions>();
        services.AddOptions<HostStartupOptions>();
        services.AddBuiltInCommands();
        services.AddProfiles();
        services.AddSingleton(Substitute.For<IStartInitializationRunner>());

        // Stub the workload subsystem so commands that depend on it (e.g.
        // WorkloadListCommand) resolve without booting real storage / loading.
        // Tests that exercise listing register their own substitutes.
        IWorkloadStore emptyStore = Substitute.For<IWorkloadStore>();
        emptyStore.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns([]);
        services.AddSingleton(emptyStore);
        services.AddSingleton(Substitute.For<IWorkloadLoader>());

        // Loaded workloads are registered individually as singletons at host
        // startup by WorkloadRegistration and surfaced via IWorkloadProvider.
        // Tests don't go through that path, so register a stub provider that
        // returns no workloads. Tests that need a populated set replace it.
        IWorkloadProvider emptyProvider = Substitute.For<IWorkloadProvider>();
        emptyProvider.GetWorkloads().Returns([]);
        emptyProvider.GetRuntimeWorkloads().Returns([]);
        emptyProvider.GetRuntimeWorkloadsByPackageId(Arg.Any<string>()).Returns([]);
        emptyProvider.GetContentWorkloads().Returns([]);
        emptyProvider.GetContentWorkloadsByPackageId(Arg.Any<string>()).Returns([]);
        services.AddSingleton(emptyProvider);

        // Workload install pipeline: substitute the installer so commands
        // resolve without standing up the real install/uninstall side effects.
        services.AddSingleton(Substitute.For<Cli.Workloads.Install.IWorkloadInstaller>());
        services.AddSingleton(Substitute.For<Cli.Workloads.Catalog.IWorkloadCatalog>());

        // Stub quickstart dependencies so QuickstartCommand resolves without
        // hitting the CDN or spawning git. Tests that exercise quickstart
        // behaviour construct the command directly with their own mocks.
        services.AddSingleton(Substitute.For<IQuickstartManifestClient>());
        services.AddSingleton(Substitute.For<IQuickstartScaffolder>());
        services.AddSingleton(Substitute.For<IGitRunner>());
        services.AddSingleton<QuickstartListCommand>();
        services.AddSingleton<QuickstartInfoCommand>();

        return services;
    }
}
