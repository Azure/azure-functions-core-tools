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
    {
        builder.Services.AddSingleton<IProjectInitializer, T>();
        return builder;
    }

    /// <summary>Registers a template provider that extends <c>func new</c>.</summary>
    public static IWorkloadBuilder AddTemplateProvider<T>(this IWorkloadBuilder builder)
        where T : class, ITemplateProvider
    {
        builder.Services.AddSingleton<ITemplateProvider, T>();
        return builder;
    }

    /// <summary>Registers a contributor that adds new subcommands to the root.</summary>
    public static IWorkloadBuilder AddCommandContributor<T>(this IWorkloadBuilder builder)
        where T : class, ICommandContributor
    {
        builder.Services.AddSingleton<ICommandContributor, T>();
        return builder;
    }
}
