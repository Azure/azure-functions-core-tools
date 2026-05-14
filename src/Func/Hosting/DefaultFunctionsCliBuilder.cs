// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Default <see cref="FunctionsCliBuilder"/> implementation. Each workload
/// gets its own instance scoped to its <see cref="WorkloadInfo"/>; the
/// underlying <see cref="IServiceCollection"/> is the global container.
/// </summary>
/// <remarks>
/// A builder constructed without a workload is used during host bootstrap;
/// calling <c>RegisterCommand</c> or <c>RegisterProjectResolver</c> on it
/// throws so an untracked contribution can never reach the parser.
/// </remarks>
internal sealed class DefaultFunctionsCliBuilder(IServiceCollection services, WorkloadInfo? workload) : FunctionsCliBuilder
{
    private readonly WorkloadInfo? _workload = workload;

    public DefaultFunctionsCliBuilder(IServiceCollection services)
        : this(services, workload: null)
    {
    }

    public override IServiceCollection Services { get; } = services ?? throw new ArgumentNullException(nameof(services));

    public override void RegisterCommand(FuncCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        RegisterCommandFactory(_ => command);
    }

    public override void RegisterCommand<TCommand>()
    {
        if (typeof(TCommand).IsAbstract)
        {
            throw new ArgumentException(
                $"Cannot register abstract command type '{typeof(TCommand)}'. Pass a concrete FuncCommand subclass.",
                nameof(TCommand));
        }

        RegisterCommandFactory(ActivatorUtilities.GetServiceOrCreateInstance<TCommand>);
    }

    public override void RegisterCommand(Type commandType)
    {
        ArgumentNullException.ThrowIfNull(commandType);

        if (!typeof(FuncCommand).IsAssignableFrom(commandType))
        {
            throw new ArgumentException(
                $"Type '{commandType}' is not assignable to {nameof(FuncCommand)}.",
                nameof(commandType));
        }

        if (commandType.IsAbstract)
        {
            throw new ArgumentException(
                $"Cannot register abstract command type '{commandType}'. Pass a concrete FuncCommand subclass.",
                nameof(commandType));
        }

        RegisterCommandFactory(sp => (FuncCommand)ActivatorUtilities.GetServiceOrCreateInstance(sp, commandType));
    }

    public override void RegisterProjectResolver(IProjectResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        WorkloadInfo workload = RequireWorkload();
        Services.AddSingleton(new WorkloadProjectResolverContribution(workload, resolver));
    }

    private void RegisterCommandFactory(Func<IServiceProvider, FuncCommand> factory)
    {
        WorkloadInfo workload = RequireWorkload();
        Services.AddSingleton<FuncCliCommand>(sp =>
        {
            FuncCommand command = factory(sp)
                ?? throw new InvalidOperationException(
                    $"Factory registered by workload '{workload.PackageId}' returned null.");
            return new ExternalCommand(workload, command);
        });
    }

    private WorkloadInfo RequireWorkload()
        => _workload ?? throw new InvalidOperationException(
            "Workload contributions can only be registered through a workload-scoped builder. " +
            "Calling Register* on the host's global builder is a CLI bug.");
}
