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
        OnRegisterCommand(command);
    }

    /// <summary>
    /// Registers a top-level command by type. The command is constructed by
    /// the DI container, so it can request services through its constructor.
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

        OnRegisterCommand<TCommand>();
    }

    /// <summary>
    /// Registers a top-level command produced by <paramref name="factory"/>.
    /// Useful when the command needs services not registered through
    /// <see cref="Services"/>, or when constructing the command requires
    /// custom logic.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <c>null</c>.</exception>
    public void RegisterCommand(Func<IServiceProvider, FuncCommand> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        OnRegisterCommand(factory);
    }

    /// <summary>Hook implemented by the host to wire a <see cref="FuncCommand"/> instance.</summary>
    protected abstract void OnRegisterCommand(FuncCommand command);

    /// <summary>Hook implemented by the host to wire a <see cref="FuncCommand"/> by type.</summary>
    protected abstract void OnRegisterCommand<TCommand>()
        where TCommand : FuncCommand;

    /// <summary>Hook implemented by the host to wire a <see cref="FuncCommand"/> via a factory.</summary>
    protected abstract void OnRegisterCommand(Func<IServiceProvider, FuncCommand> factory);
}

