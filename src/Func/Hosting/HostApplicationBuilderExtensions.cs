// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
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
    /// becomes a stderr warning and the remaining workloads still load. The
    /// caller resolves <see cref="IInteractionService"/> from
    /// <see cref="HostApplicationBuilder.Services"/>; it must already be
    /// registered (it is, by <see cref="CliHost.CreateBuilder"/>).
    /// </remarks>
    public static Task RegisterWorkloadsAsync(
        this HostApplicationBuilder builder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);

        IInteractionService interaction = ResolveInteractionService(builder);
        return WorkloadRegistration.RegisterWorkloadsAsync(
            builder.Services,
            builder.Configuration,
            interaction,
            cancellationToken);
    }

    private static IInteractionService ResolveInteractionService(HostApplicationBuilder builder)
    {
        // The singleton instance was added by CliHost.CreateBuilder; pull it
        // back out without building the full provider so we don't capture
        // descriptors that workloads will add a moment later.
        foreach (ServiceDescriptor descriptor in builder.Services)
        {
            if (descriptor.ServiceType == typeof(IInteractionService)
                && descriptor.ImplementationInstance is IInteractionService instance)
            {
                return instance;
            }
        }

        throw new InvalidOperationException(
            $"{nameof(IInteractionService)} singleton is not registered. " +
            $"Use {nameof(CliHostFactory)}.{nameof(CliHostFactory.CreateBuilder)} to build the host.");
    }
}
