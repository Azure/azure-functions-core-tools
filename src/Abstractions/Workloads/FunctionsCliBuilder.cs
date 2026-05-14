// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Bootstrap surface passed to <see cref="Workload.Configure"/>.
/// Workloads register their services, project initializers, commands, and
/// detectors through <see cref="Services"/> and the <c>Register*</c> methods.
/// </summary>
/// <remarks>
/// Modeled after WebJobs' <c>IWebJobsBuilder</c>: the builder exposes the DI
/// container so workloads can use any standard .NET DI extension method, and
/// the host picks up the registered services after every workload has been
/// configured. Abstract class (rather than an interface) so we can grow the
/// surface with new properties or virtual members without breaking existing
/// workloads.
/// </remarks>
public abstract class FunctionsCliBuilder
{
    /// <summary>
    /// The DI service collection workloads contribute to.
    /// </summary>
    public abstract IServiceCollection Services { get; }

    /// <summary>
    /// Registers a top-level <see cref="FuncCommand"/> instance. The same
    /// instance is used for every invocation; do not depend on per-invocation
    /// state being held on the command itself.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <c>null</c>.</exception>
    public abstract void RegisterCommand(FuncCommand command);

    /// <summary>
    /// Registers a top-level command by type. The command is constructed
    /// through <see cref="ActivatorUtilities.GetServiceOrCreateInstance(IServiceProvider, Type)"/>,
    /// so it can request services through its constructor without the
    /// workload needing to register the command type itself with
    /// <see cref="Services"/>.
    /// </summary>
    /// <typeparam name="TCommand">A concrete <see cref="FuncCommand"/>.</typeparam>
    /// <exception cref="ArgumentException"><typeparamref name="TCommand"/> is abstract.</exception>
    public abstract void RegisterCommand<TCommand>()
        where TCommand : FuncCommand;

    /// <summary>
    /// Registers a top-level command by runtime <see cref="Type"/>. Useful
    /// when the command type is only known at runtime (e.g. discovered from
    /// configuration).
    /// </summary>
    /// <param name="commandType">A concrete type assignable to <see cref="FuncCommand"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="commandType"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="commandType"/> is not assignable to <see cref="FuncCommand"/>, or it is abstract.
    /// </exception>
    public abstract void RegisterCommand(Type commandType);

    /// <summary>
    /// Registers an <see cref="IProjectDetector"/> that participates in
    /// workload resolution for the current project (init, new, pack, start).
    /// The host tags the registration with the calling workload so resolver
    /// diagnostics can attribute results to their owner.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="detector"/> is <c>null</c>.</exception>
    public abstract void RegisterProjectDetector(IProjectDetector detector);
}
