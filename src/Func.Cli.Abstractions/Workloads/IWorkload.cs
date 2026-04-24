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
/// <see cref="ICommandProvider"/> to add brand-new commands.
/// </summary>
public interface IWorkload
{
    /// <summary>
    /// Globally unique package identifier — typically the assembly / NuGet
    /// package name (e.g. <c>"Azure.Functions.Cli.Workload.Dotnet"</c>). This
    /// is what <c>func workload install/uninstall</c> consumes; user-facing
    /// short names (<c>-w dotnet</c>) come from each contribution's
    /// <see cref="IProjectInitializer.WorkerRuntime"/> /
    /// <see cref="ITemplateProvider.WorkerRuntime"/>.
    /// </summary>
    public string PackageId { get; }

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
