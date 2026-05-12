// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Install;

/// <summary>
/// Coordinates installing and uninstalling workloads on disk and in the
/// global workload registry.
/// </summary>
internal interface IWorkloadInstaller
{
    /// <summary>
    /// Installs a workload from the <c>.nupkg</c> at <paramref name="nupkgPath"/>.
    /// The package is extracted into <see cref="IWorkloadPaths.GetInstallDirectory"/>
    /// and recorded in the global registry; the source <c>.nupkg</c> is left untouched.
    /// </summary>
    /// <param name="nupkgPath">Path to a <c>.nupkg</c> on disk.</param>
    /// <param name="force">
    /// When <c>true</c>, an existing install of the same id+version is removed
    /// before extraction.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the install.</param>
    /// <returns>
    /// The registry entry plus a flag indicating whether the install was a
    /// no-op because the same <c>(packageId, version)</c> was already present.
    /// </returns>
    /// <exception cref="FileNotFoundException">The <c>.nupkg</c> does not exist.</exception>
    /// <exception cref="Discovery.InvalidWorkloadException">
    /// The package is unreadable, missing the <c>FuncCliWorkload</c> package
    /// type, or its <c>workload.json</c> is missing or malformed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The install directory exists without a matching registry entry and
    /// <paramref name="force"/> is <c>false</c>.
    /// </exception>
    public Task<WorkloadInstallResult> InstallFromPackageAsync(
        string nupkgPath,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves <paramref name="packageId"/> against the configured catalog,
    /// downloads the matching <c>.nupkg</c> to a temp file, and routes it
    /// through <see cref="InstallFromPackageAsync"/> so disk + registry
    /// behaviour matches local installs exactly.
    /// </summary>
    /// <param name="packageId">
    /// User-supplied package id or alias; case-insensitive. Treated as a
    /// literal package id when <paramref name="exact"/> is <c>true</c>;
    /// otherwise the catalog's <c>alias:&lt;name&gt;</c> tag is consulted
    /// first (spec §6.1 step 2).
    /// </param>
    /// <param name="version">
    /// Optional explicit version. When non-null the catalog must have an
    /// exact match; when null the highest available version under
    /// <paramref name="includePrerelease"/> is selected.
    /// </param>
    /// <param name="source">Optional <c>--source</c> override forwarded to the catalog.</param>
    /// <param name="includePrerelease"><c>true</c> to allow prerelease versions when resolving.</param>
    /// <param name="exact">
    /// <c>true</c> to disable alias matching and treat
    /// <paramref name="packageId"/> as a literal package id.
    /// </param>
    /// <param name="force">Forwarded to <see cref="InstallFromPackageAsync"/>.</param>
    /// <param name="cancellationToken">Token to cancel resolve, download, and install.</param>
    /// <exception cref="WorkloadPackageNotFoundException">
    /// No version matched on any configured source.
    /// </exception>
    /// <exception cref="AmbiguousAliasException">
    /// The alias matches multiple packages and <paramref name="exact"/> is
    /// <c>false</c>.
    /// </exception>
    public Task<WorkloadInstallResult> InstallFromCatalogAsync(
        string packageId,
        NuGetVersion? version,
        string? source,
        bool includePrerelease,
        bool exact,
        bool force,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// In-place version swap for an installed workload (spec §6.4). Stages
    /// and validates a new version before touching the existing install;
    /// on failure the existing version remains live.
    /// </summary>
    /// <param name="packageId">Installed workload id; case-insensitive.</param>
    /// <param name="targetInstalledVersion">
    /// Optional installed version to replace. When null the highest
    /// installed semver is targeted.
    /// </param>
    /// <param name="source">Optional <c>--source</c> override.</param>
    /// <param name="includePrerelease"><c>true</c> to allow prerelease candidates.</param>
    /// <param name="allowMajor">
    /// <c>true</c> to allow the new version to cross a major boundary.
    /// Default is <c>false</c> per spec; <c>--major</c> on the command sets
    /// it to <c>true</c>.
    /// </param>
    /// <param name="cancellationToken">Token propagated to catalog + I/O.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="packageId"/> is not installed, or
    /// <paramref name="targetInstalledVersion"/> is not present.
    /// </exception>
    public Task<WorkloadUpdateResult> UpdateAsync(
        string packageId,
        NuGetVersion? targetInstalledVersion,
        string? source,
        bool includePrerelease,
        bool allowMajor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the install directory and registry entry for
    /// (<paramref name="packageId"/>, <paramref name="version"/>). Returns
    /// <c>true</c> when an entry was removed, <c>false</c> when none existed.
    /// </summary>
    public Task<bool> UninstallAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default);
}
