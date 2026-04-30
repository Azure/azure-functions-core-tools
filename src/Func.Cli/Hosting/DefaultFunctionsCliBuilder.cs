// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Default <see cref="FunctionsCliBuilder"/> implementation. Internal so
/// workloads only see the abstract base type.
///
/// Each invocation of <see cref="IWorkload.Configure"/> gets its own instance
/// constructed with the corresponding <see cref="WorkloadInfo"/>; the
/// underlying <see cref="IServiceCollection"/> is the same global container
/// the host uses, so workloads contributing the same service interface all
/// flow into <c>GetServices&lt;T&gt;()</c>. The per-workload instance only
/// scopes the command-tracking metadata — when
/// <see cref="FunctionsCliBuilder.RegisterCommand(FuncCommand)"/> (or one of
/// its overloads) is called, it produces an <see cref="ExternalCommand"/>
/// tagged with the calling workload's <see cref="WorkloadInfo"/>.
///
/// A builder can also be constructed without a workload (used during host
/// bootstrap before any workload has been loaded). Calls to
/// <c>RegisterCommand</c> on a non-workload builder fail fast with a clear
/// error so an untracked command can never reach the parser.
/// </summary>
internal sealed class DefaultFunctionsCliBuilder : FunctionsCliBuilder
{
    private readonly WorkloadInfo? _workload;

    public DefaultFunctionsCliBuilder(IServiceCollection services)
        : this(services, workload: null)
    {
    }

    public DefaultFunctionsCliBuilder(IServiceCollection services, WorkloadInfo? workload)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        _workload = workload;
    }

    public override IServiceCollection Services { get; }

    protected override void OnRegisterCommand(FuncCommand command)
    {
        var workload = RequireWorkload();
        Services.AddSingleton<BaseCommand>(_ => new ExternalCommand(workload, command));
    }

    protected override void OnRegisterCommand<TCommand>()
    {
        var workload = RequireWorkload();
        Services.AddSingleton<TCommand>();
        Services.AddSingleton<BaseCommand>(sp =>
            new ExternalCommand(workload, sp.GetRequiredService<TCommand>()));
    }

    protected override void OnRegisterCommand(Func<IServiceProvider, FuncCommand> factory)
    {
        var workload = RequireWorkload();
        Services.AddSingleton<BaseCommand>(sp =>
        {
            var command = factory(sp)
                ?? throw new InvalidOperationException(
                    $"Factory registered by workload '{workload.PackageId}' returned null.");
            return new ExternalCommand(workload, command);
        });
    }

    private WorkloadInfo RequireWorkload()
        => _workload ?? throw new InvalidOperationException(
            "Commands can only be registered through a workload-scoped builder. " +
            "RegisterCommand is invoked by the workload loader during IWorkload.Configure; " +
            "calling it on the host's global builder is a CLI bug.");
}

