// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Install;

/// <summary>
/// Coordinates installing and uninstalling workloads on disk + in the global
/// manifest. Behind an interface so the install / uninstall commands stay
/// thin shells that don't deal with filesystem state directly.
/// </summary>
internal interface IWorkloadInstaller
{
    /// <summary>
    /// Installs a workload from <paramref name="packageDirectory"/> (an
    /// already-extracted .nupkg). The directory is consumed: on success it
    /// is moved into <see cref="IWorkloadPaths.GetInstallDirectory"/> and is
    /// no longer addressable at the original path.
    /// </summary>
    /// <exception cref="Common.GracefulException">
    /// Thrown for: missing nuspec; missing required nuspec metadata; the
    /// package not declaring the <c>FuncCliWorkload</c> package type; the
    /// install directory already existing; the entry-point scan failing.
    /// </exception>
    public Task<InstalledWorkload> InstallFromDirectoryAsync(
        string packageDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the install directory for (<paramref name="packageId"/>,
    /// <paramref name="version"/>) and drops the manifest entry. Returns
    /// <c>true</c> when an entry was removed, <c>false</c> when no such
    /// entry existed.
    /// </summary>
    public Task<bool> UninstallAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default);
}
