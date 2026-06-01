// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Read-only view over installed templates content-workload rows in the
/// global workload registry. Mirrors <c>IInstalledBundleWorkloads</c>: the
/// orchestrator uses this to walk per-stack installed templates packages
/// without touching the registry directly.
/// </summary>
public interface IInstalledTemplatesWorkloads
{
    /// <summary>
    /// NuGet package-id prefix for templates content workloads. Concrete ids
    /// are <c>{Prefix}.&lt;Stack&gt;</c> (e.g. <c>Azure.Functions.Cli.Workloads.Templates.Node</c>).
    /// Matched case-insensitively at lookup time; registry stores them lowercased
    /// per NuGet normalization.
    /// </summary>
    public const string TemplatesWorkloadPackageIdPrefix = "Azure.Functions.Cli.Workloads.Templates";

    /// <summary>
    /// Returns every installed templates workload row whose package id matches
    /// <c>{Prefix}.&lt;Stack&gt;</c> for the supplied <paramref name="stack"/>
    /// (case-insensitive). Multiple rows may be returned when several
    /// versions of the same stack's workload are installed side-by-side; the
    /// orchestrator picks the matching channel.
    /// </summary>
    public Task<IReadOnlyList<InstalledTemplatesWorkload>> ListInstalledAsync(
        string stack,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One installed templates workload row.
/// </summary>
/// <param name="Stack">The stack this workload targets (lowercased).</param>
/// <param name="PackageVersion">
/// Installed package version. Prerelease label encodes the channel
/// (no label = stable, <c>-preview</c>, <c>-experimental</c>).
/// </param>
/// <param name="InstallDirectory">
/// Absolute path to the workload's extracted install dir
/// (<c>&lt;workload-home&gt;/workloads/&lt;packageId&gt;/&lt;packageVersion&gt;</c>).
/// The templates content lives under <c>tools/any/content/</c> within this
/// directory.
/// </param>
public sealed record InstalledTemplatesWorkload(
    string Stack,
    string PackageVersion,
    string InstallDirectory);
