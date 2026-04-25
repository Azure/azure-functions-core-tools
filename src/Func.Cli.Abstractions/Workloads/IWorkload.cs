// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Implemented by a workload's entry-point class. The CLI loader instantiates
/// the type named by the workload's manifest (<c>workload.json</c>
/// <c>entryPoint</c>), then invokes <see cref="Configure"/> so the workload
/// can register its services with <see cref="IFunctionsCliBuilder"/>.
///
/// Mirrors the shape of WebJobs' <c>IWebJobsStartup</c>. Implementations must
/// have a parameterless constructor.
/// </summary>
public interface IWorkload
{
    /// <summary>
    /// Globally unique package identifier — typically the assembly / NuGet
    /// package name (e.g. <c>"Azure.Functions.Cli.Workload.Dotnet"</c>).
    /// Used to identify the workload in the manifest and CLI output.
    /// </summary>
    public string PackageId { get; }

    /// <summary>
    /// Workload version. Authored on the workload itself rather than read
    /// from the NuGet package metadata so the running code is the source of
    /// truth for what version of the workload is installed.
    /// </summary>
    public string PackageVersion { get; }

    /// <summary>
    /// Workload category (stack / tool / extension). Surfaces in
    /// <c>func workload list</c> and lets the CLI group or filter workloads
    /// by kind.
    /// </summary>
    public WorkloadType Type { get; }

    /// <summary>Human-readable name shown in <c>func workload list</c>.</summary>
    public string DisplayName { get; }

    /// <summary>One-line description of what the workload provides.</summary>
    public string Description { get; }

    /// <summary>
    /// Registers the workload's services with the host. Called once during
    /// CLI bootstrap, before the root command tree is built.
    /// </summary>
    public void Configure(IFunctionsCliBuilder builder);
}
