// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Bootstrap surface passed to <see cref="Workload.Configure"/>. Workloads
/// register services, project initializers, commands, and detectors through
/// <see cref="Services"/> and the <c>Register*</c> methods.
/// </summary>
/// <remarks>
/// Modeled after WebJobs' <c>IWebJobsBuilder</c>. Abstract class rather than
/// an interface so the surface can grow without breaking workloads.
/// </remarks>
public abstract class FunctionsCliBuilder
{
    public abstract IServiceCollection Services { get; }

    /// <summary>
    /// Registers a top-level <see cref="FuncCommand"/> instance. The same
    /// instance is reused for every invocation.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <c>null</c>.</exception>
    public abstract void RegisterCommand(FuncCommand command);

    /// <summary>
    /// Registers a top-level command by type. Constructed through
    /// <see cref="ActivatorUtilities.GetServiceOrCreateInstance(IServiceProvider, Type)"/>,
    /// so it can take dependencies through its constructor.
    /// </summary>
    /// <typeparam name="TCommand">A concrete <see cref="FuncCommand"/>.</typeparam>
    /// <exception cref="ArgumentException"><typeparamref name="TCommand"/> is abstract.</exception>
    public abstract void RegisterCommand<TCommand>()
        where TCommand : FuncCommand;

    /// <summary>
    /// Registers a top-level command by runtime <see cref="Type"/>.
    /// </summary>
    /// <param name="commandType">A concrete type assignable to <see cref="FuncCommand"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="commandType"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="commandType"/> is not assignable to <see cref="FuncCommand"/>, or it is abstract.
    /// </exception>
    public abstract void RegisterCommand(Type commandType);

    /// <summary>
    /// Registers an <see cref="IProjectDetector"/> that participates in
    /// workload resolution. The host tags the registration with the calling
    /// workload.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="detector"/> is <c>null</c>.</exception>
    public abstract void RegisterProjectDetector(IProjectDetector detector);
}
