// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Bootstraps the Func CLI's DI container. Mirrors the shape of
/// <c>HostApplicationBuilder</c> but stays minimal: a CLI process exits after
/// one command, so we don't need the full hosting stack.
///
/// Workloads are registered via <see cref="AddWorkload"/>; their <see cref="IWorkload.Register"/>
/// runs immediately so all services they contribute are visible to the
/// command-tree composition that follows.
/// </summary>
public sealed class FuncCliHostBuilder
{
    private readonly List<IWorkload> _workloads = new();

    public FuncCliHostBuilder(IInteractionService interaction)
    {
        Services = new ServiceCollection();

        // Built-in singletons every command may consume.
        Services.AddSingleton(interaction);
        Services.AddSingleton<IReadOnlyList<IWorkload>>(_ => _workloads);

        // Lazy: aliases come from each workload's contributions, which aren't
        // resolvable until the provider is built.
        Services.AddSingleton<IReadOnlyList<WorkloadSummary>>(sp =>
        {
            var initializers = sp.GetServices<WorkloadContribution<IProjectInitializer>>().ToList();
            var templates = sp.GetServices<WorkloadContribution<ITemplateProvider>>().ToList();

            return _workloads
                .Select(w =>
                {
                    var aliases = initializers.Where(c => ReferenceEquals(c.Owner, w)).Select(c => c.Service.WorkerRuntime)
                        .Concat(templates.Where(c => ReferenceEquals(c.Owner, w)).Select(c => c.Service.WorkerRuntime))
                        .Where(a => !string.IsNullOrWhiteSpace(a))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    return new WorkloadSummary(w.PackageId, w.DisplayName, w.Description, aliases);
                })
                .ToList();
        });
    }

    /// <summary>The DI service collection. Exposed for advanced bootstrap scenarios.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Registers a workload and immediately invokes its <see cref="IWorkload.Register"/>
    /// so its contributions are part of the container that <see cref="Build"/> produces.
    /// </summary>
    public FuncCliHostBuilder AddWorkload(IWorkload workload)
    {
        ArgumentNullException.ThrowIfNull(workload);

        _workloads.Add(workload);
        var builder = new WorkloadBuilder(workload, Services);
        workload.Register(builder);
        return this;
    }

    /// <summary>Builds the service provider.</summary>
    public ServiceProvider Build() => Services.BuildServiceProvider();
}
