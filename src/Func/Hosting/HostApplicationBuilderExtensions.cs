// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// CLI-specific extensions on <see cref="HostApplicationBuilder"/> so the
/// boot pipeline reads as a fluent sequence in <c>Program.cs</c> and tests.
/// </summary>
internal static class HostApplicationBuilderExtensions
{
    /// <summary>
    /// Loads installed workloads from <c>~/.azure-functions/workloads.json</c>
    /// and lets each <see cref="Workloads.Workload.Configure"/> contribute
    /// services to <paramref name="builder"/>. Must run before
    /// <see cref="HostApplicationBuilder.Build"/> so the service collection is
    /// still mutable.
    /// </summary>
    /// <remarks>
    /// Per-workload load and Configure failures are isolated: a single throw
    /// becomes a stderr warning and the remaining workloads still load.
    /// Workload home defaults to whatever
    /// <see cref="WorkloadHomeResolver.Resolve"/> returns; integration tests
    /// override by registering a <see cref="WorkloadPathsOptions"/> instance
    /// on <see cref="HostApplicationBuilder.Services"/> after
    /// <see cref="CliHostFactory.CreateBuilder"/>.
    /// </remarks>
    public static Task RegisterWorkloadsAsync(this HostApplicationBuilder builder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);

        IInteractionService interaction = ResolveSingletonInstance<IInteractionService>(builder);
        WorkloadPathsOptions paths = TryResolveSingletonInstance<WorkloadPathsOptions>(builder)
            ?? new WorkloadPathsOptions();
        return WorkloadRegistration.RegisterWorkloadsAsync(builder.Services, paths, interaction, cancellationToken);
    }

    /// <summary>
    /// Pulls a singleton instance back out of the service descriptors without
    /// building the full provider, so we don't capture descriptors workloads
    /// will add a moment later. Returns the last registered instance, matching
    /// the lifetime semantics tests rely on when they replace a default.
    /// </summary>
    private static T ResolveSingletonInstance<T>(HostApplicationBuilder builder)
        where T : class
    {
        T? instance = TryResolveSingletonInstance<T>(builder);
        if (instance is not null)
        {
            return instance;
        }

        throw new InvalidOperationException(
            $"{typeof(T).Name} singleton is not registered. " +
            $"Use {nameof(CliHostFactory)}.{nameof(CliHostFactory.CreateBuilder)} to build the host.");
    }

    /// <summary>
    /// Same descriptor scan as <see cref="ResolveSingletonInstance{T}"/>, but
    /// returns <see langword="null"/> when no instance descriptor is found.
    /// Used for optional overrides (e.g. tests substituting
    /// <see cref="WorkloadPathsOptions"/>); a missing registration is not an
    /// error, the caller constructs a default.
    /// </summary>
    private static T? TryResolveSingletonInstance<T>(HostApplicationBuilder builder)
        where T : class
    {
        T? instance = null;
        foreach (ServiceDescriptor descriptor in builder.Services)
        {
            if (descriptor.ServiceType == typeof(T)
                && descriptor.ImplementationInstance is T candidate)
            {
                instance = candidate;
            }
        }

        return instance;
    }
}
