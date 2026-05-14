// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Default <see cref="FunctionsCliBuilder"/> implementation. Internal so
/// workloads only see the abstract base type.
/// </summary>
/// <remarks>
/// Each invocation of <see cref="Workload.Configure"/> gets its own instance
/// constructed with the corresponding <see cref="WorkloadInfo"/>; the
/// underlying <see cref="IServiceCollection"/> is the same global container
/// the host uses, so workloads contributing the same service interface all
/// flow into <c>GetServices&lt;T&gt;()</c>. The per-workload instance only
/// scopes the command-tracking metadata: when <c>RegisterCommand</c> is
/// called, it produces an <see cref="ExternalCommand"/> tagged with the
/// calling workload's <see cref="WorkloadInfo"/>.
/// <para>
/// A builder can also be constructed without a workload (used during host
/// bootstrap before any workload has been loaded). Calls to
/// <c>RegisterCommand</c> or <c>RegisterProjectDetector</c> on a non-workload
/// builder fail fast with a clear error so an untracked contribution can
/// never reach the parser.
/// </para>
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

    public override void RegisterProjectDetector(IProjectDetector detector)
    {
        ArgumentNullException.ThrowIfNull(detector);
        WorkloadInfo workload = RequireWorkload();
        Services.AddSingleton(new WorkloadDetectorContribution(workload, detector));
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
            "RegisterCommand / RegisterProjectDetector are invoked by the workload loader during Workload.Configure; " +
            "calling them on the host's global builder is a CLI bug.");
}
