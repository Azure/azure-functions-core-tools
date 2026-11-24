// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Base class for a workload's entry-point. The CLI loader instantiates the
/// type identified by the workload's manifest (<c>workload.json</c> in the
/// package root), then invokes <see cref="Configure"/> so the workload can
/// register its services with <see cref="FunctionsCliBuilder"/>.
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
/// registry. Its presentation and optional stack descriptor come from the
/// package manifest, not from executing the workload.
/// </remarks>
public abstract class Workload
{
    /// <summary>
    /// Registers the workload's services with the host. Called once during
    /// CLI bootstrap, before the root command tree is built.
    /// </summary>
    public abstract void Configure(FunctionsCliBuilder builder);
}
