// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Bootstrap surface passed to <see cref="IWorkload.Configure"/>.
/// Workloads register their services — project initializers, commands, and
/// any other supporting types — through <see cref="Services"/> and the
/// <see cref="RegisterCommand(FuncCommand)"/> overloads.
///
/// Modeled after WebJobs' <c>IWebJobsBuilder</c>: the builder exposes the DI
/// container so workloads can use any standard .NET DI extension method, and
/// the host picks up the registered services after every workload has been
/// configured.
///
/// Abstract class (rather than an interface) so we can grow the surface with
/// new properties or virtual members without breaking existing workloads.
/// </summary>
public abstract class FunctionsCliBuilder
{
    /// <summary>The DI service collection workloads contribute to.</summary>
    public abstract IServiceCollection Services { get; }

    /// <summary>
    /// Registers a top-level <see cref="FuncCommand"/> instance. The same
    /// instance is used for every invocation; do not depend on per-invocation
    /// state being held on the command itself.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <c>null</c>.</exception>
    public void RegisterCommand(FuncCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        OnRegisterCommand(_ => command);
    }

    /// <summary>
    /// Registers a top-level command by type. The command is constructed
    /// through <see cref="ActivatorUtilities.GetServiceOrCreateInstance(IServiceProvider, Type)"/>,
    /// so it can request services through its constructor without the
    /// workload needing to register the command type itself with
    /// <see cref="Services"/>.
    /// </summary>
    /// <typeparam name="TCommand">A concrete <see cref="FuncCommand"/>.</typeparam>
    /// <exception cref="ArgumentException"><typeparamref name="TCommand"/> is abstract.</exception>
    public void RegisterCommand<TCommand>()
        where TCommand : FuncCommand
    {
        if (typeof(TCommand).IsAbstract)
        {
            throw new ArgumentException(
                $"Cannot register abstract command type '{typeof(TCommand)}'. Pass a concrete FuncCommand subclass.",
                nameof(TCommand));
        }

        OnRegisterCommand(sp => ActivatorUtilities.GetServiceOrCreateInstance<TCommand>(sp));
    }

    /// <summary>
    /// Registers a top-level command by runtime <see cref="Type"/>. Useful
    /// when the command type is only known at runtime (e.g. discovered from
    /// configuration). The type must be a concrete <see cref="FuncCommand"/>;
    /// it is constructed through
    /// <see cref="ActivatorUtilities.GetServiceOrCreateInstance(IServiceProvider, Type)"/>.
    /// </summary>
    /// <param name="commandType">A concrete type assignable to <see cref="FuncCommand"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="commandType"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="commandType"/> is not assignable to <see cref="FuncCommand"/>, or it is abstract.
    /// </exception>
    public void RegisterCommand(Type commandType)
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

        OnRegisterCommand(sp => (FuncCommand)ActivatorUtilities.GetServiceOrCreateInstance(sp, commandType));
    }

    /// <summary>
    /// Hook implemented by the host to wire a <see cref="FuncCommand"/>
    /// produced by <paramref name="factory"/> into the parser. The factory
    /// is invoked when the host resolves commands; it is given the host's
    /// <see cref="IServiceProvider"/> so the command can request services.
    /// </summary>
    protected abstract void OnRegisterCommand(Func<IServiceProvider, FuncCommand> factory);
}


