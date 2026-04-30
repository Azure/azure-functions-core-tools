// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Tests;

/// <summary>
/// Test helper that builds the same DI shape Program.cs builds at runtime —
/// built-in commands + an empty workload list — so tests can call
/// <see cref="Parser.CreateCommand"/> without standing up a host.
/// </summary>
internal static class TestParser
{
    public static FuncRootCommand CreateRoot(IInteractionService interaction)
    {
        var services = BuildBaseServices(interaction);
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
        WorkloadInfo workload,
        Action<FunctionsCliBuilder> configure)
    {
        var services = BuildBaseServices(interaction);
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
        var services = BuildBaseServices(interaction);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static ServiceCollection BuildBaseServices(IInteractionService interaction)
    {
        var services = new ServiceCollection();
        services.AddSingleton(interaction);
        services.AddBuiltInCommands();
        services.AddSingleton<IReadOnlyList<WorkloadInfo>>(Array.Empty<WorkloadInfo>());
        return services;
    }
}

