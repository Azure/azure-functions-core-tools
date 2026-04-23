// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// A workload extends the Func CLI with language- or feature-specific behavior.
/// Implementations must have a parameterless constructor; the CLI instantiates
/// the workload during bootstrap and calls <see cref="Register"/>, where the
/// workload contributes services to the DI container.
///
/// What a workload contributes is expressed by the services it registers:
/// register an <see cref="IProjectInitializer"/> to extend <c>func init</c>,
/// an <see cref="ITemplateProvider"/> to extend <c>func new</c>, or an
/// <see cref="ICommandContributor"/> to add brand-new commands.
/// </summary>
public interface IWorkload
{
    /// <summary>Stable, lowercase identifier (e.g. "dotnet", "node").</summary>
    public string Id { get; }

    /// <summary>Human-readable name shown in <c>func workload list</c>.</summary>
    public string DisplayName { get; }

    /// <summary>One-line description of what the workload provides.</summary>
    public string Description { get; }

    /// <summary>
    /// Registers the workload's services with the DI container. Called once
    /// during CLI bootstrap, before the root command tree is built.
    /// </summary>
    public void Register(IWorkloadBuilder builder);
}
