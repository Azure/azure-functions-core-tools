// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

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
    /// Conventional package-ID prefix used for templates content workloads.
    /// </summary>
    public const string TemplatesWorkloadPackageIdPrefix = "Azure.Functions.Cli.Workloads.Templates";

    /// <summary>
    /// Returns installed templates rows for the supplied stack across channels.
    /// </summary>
    public Task<IReadOnlyList<InstalledTemplatesWorkload>> ListInstalledAsync(
        string stack,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns portable templates rows for one owner in the requested channel, or across channels when omitted.
    /// </summary>
    /// <remarks>
    /// Logical-owner metadata takes precedence over physical metadata. A conventional package ID wins
    /// regardless of aliases. Otherwise exactly one owner must declare <c>&lt;stack&gt;-templates</c>.
    /// Portable rows have a null or empty runtime identifier, or <c>any</c> (case-insensitive).
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="stack"/> is null, empty, or whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Competing custom owners claim the selected channel, or only unsupported templates rows match.
    /// </exception>
    public Task<IReadOnlyList<InstalledTemplatesWorkload>> ListInstalledAsync(
        string stack,
        CancellationToken cancellationToken,
        BundleChannel? channel)
        => ListInstalledAsync(stack, cancellationToken);
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
/// Absolute path to the physical package's extracted install root, not its content directory
/// (<c>&lt;workload-home&gt;/workloads/&lt;packageId&gt;/&lt;packageVersion&gt;</c>).
/// The templates content lives under <c>tools/any/content/</c> within this
/// directory.
/// </param>
public sealed record InstalledTemplatesWorkload(
    string Stack,
    string PackageVersion,
    string InstallDirectory);
