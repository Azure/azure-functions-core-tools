// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;

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
public abstract class Workload
{
    /// <summary>
    /// Globally unique workload identifier, typically the assembly / NuGet
    /// package name (e.g. <c>"Azure.Functions.Cli.Workload.Dotnet"</c>).
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Workload version. Defaults to the workload assembly's
    /// <see cref="System.Reflection.AssemblyInformationalVersionAttribute"/>
    /// (falling back to <see cref="System.Reflection.AssemblyFileVersionAttribute"/>
    /// and then <see cref="System.Reflection.AssemblyName.Version"/>), so
    /// most workloads can leave this alone and let the build supply the
    /// version. Override to author the version on the workload itself when
    /// the running code should be the source of truth. Should be a valid
    /// SemVer 2.0 string (e.g. <c>"1.2.3"</c>, <c>"1.2.3-preview.1"</c>);
    /// the CLI does not currently enforce or normalize the format.
    /// </summary>
    public virtual string Version
    {
        get
        {
            Assembly assembly = GetType().Assembly;

            string? informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                // Strip any +sha build metadata so callers see a clean SemVer string.
                int plus = informational.IndexOf('+');
                return plus >= 0 ? informational[..plus] : informational;
            }

            string? file = assembly
                .GetCustomAttribute<AssemblyFileVersionAttribute>()
                ?.Version;
            if (!string.IsNullOrWhiteSpace(file))
            {
                return file;
            }

            return assembly.GetName().Version?.ToString() ?? "0.0.0";
        }
    }

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
