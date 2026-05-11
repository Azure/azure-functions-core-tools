// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;

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
    /// Removes the install directory and registry entry for
    /// (<paramref name="packageId"/>, <paramref name="version"/>). Returns
    /// <c>true</c> when an entry was removed, <c>false</c> when none existed.
    /// </summary>
    public Task<bool> UninstallAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default);
}
