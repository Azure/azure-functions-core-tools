// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Convenience helpers for registering common workload contributions. Workloads
/// can call into <see cref="IWorkloadBuilder.Services"/> directly, but these
/// extensions express intent more clearly.
/// </summary>
public static class WorkloadBuilderExtensions
{
    /// <summary>Registers a project initializer that extends <c>func init</c>.</summary>
    public static IWorkloadBuilder AddProjectInitializer<T>(this IWorkloadBuilder builder)
        where T : class, IProjectInitializer
        => builder.AddContribution<IProjectInitializer, T>();

    /// <summary>Registers a template provider that extends <c>func new</c>.</summary>
    public static IWorkloadBuilder AddTemplateProvider<T>(this IWorkloadBuilder builder)
        where T : class, ITemplateProvider
        => builder.AddContribution<ITemplateProvider, T>();

    /// <summary>Registers a provider that adds new subcommands to the root.</summary>
    public static IWorkloadBuilder AddCommandProvider<T>(this IWorkloadBuilder builder)
        where T : class, ICommandProvider
        => builder.AddContribution<ICommandProvider, T>();

    // Registers T as a singleton, exposes it through the TService collection,
    // AND records ownership via WorkloadContribution<TService> so the host can
    // later answer "which workload contributed this service?" without
    // reflection or marker interfaces.
    private static IWorkloadBuilder AddContribution<TService, TImpl>(this IWorkloadBuilder builder)
        where TService : class
        where TImpl : class, TService
    {
        builder.Services.AddSingleton<TImpl>();
        builder.Services.AddSingleton<TService>(sp => sp.GetRequiredService<TImpl>());
        builder.Services.AddSingleton<WorkloadContribution<TService>>(sp =>
            new WorkloadContribution<TService>(builder.Workload, sp.GetRequiredService<TImpl>()));
        return builder;
    }
}
