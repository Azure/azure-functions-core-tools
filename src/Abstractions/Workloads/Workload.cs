// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Base class for a workload's entry-point. The CLI loader instantiates the
/// type identified by the workload's <see cref="WorkloadMetadata"/>
/// (<c>workload.json</c> in the package root), then invokes
/// <see cref="Configure"/> so the workload can register its services with
/// <see cref="FunctionsCliBuilder"/>.
///
/// Mirrors the shape of WebJobs' <c>IWebJobsStartup</c>. Implementations must
/// have a parameterless constructor.
///
/// Abstract class (rather than an interface) so we can grow the surface with
/// new properties or virtual members without breaking existing workloads.
/// </summary>
/// <remarks>
/// The workload's identity (package id, version, aliases) is supplied by the
/// NuGet package's nuspec at install time and persisted in the global
/// registry, so the type itself only needs to describe its CLI presentation
/// and wire up its services.
/// </remarks>
public abstract class Workload
{
    /// <summary>
    /// Human-readable name shown in <c>func workload list</c>.
    /// </summary>
    public abstract string DisplayName { get; }

    /// <summary>
    /// One-line description of what the workload provides.
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// Registers the workload's services with the host. Called once during
    /// CLI bootstrap, before the root command tree is built.
    /// </summary>
    public abstract void Configure(FunctionsCliBuilder builder);
}
