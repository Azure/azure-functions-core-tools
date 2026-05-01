// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Install;

/// <summary>
/// Default <see cref="IWorkloadInstaller"/>. Composes the nuspec reader,
/// entry-point scanner, paths layout, and manifest store into a linear
/// install pipeline.
/// </summary>
/// <remarks>
/// Pipeline order is chosen so disk state stays consistent on failure:
/// validate metadata and entry point first (pure reads), then move the
/// staging directory into the final install location (atomic on a single
/// volume), then write the manifest entry. If the manifest write fails the
/// install directory is rolled back so the on-disk state matches the
/// manifest state.
/// </remarks>
internal sealed class WorkloadInstaller(
    IWorkloadPaths paths,
    IGlobalManifestStore store,
    INuspecReader nuspecReader,
    IWorkloadEntryPointScanner scanner) : IWorkloadInstaller
{
    /// <summary>
    /// Package-type name a workload package must declare in its .nuspec.
    /// </summary>
    public const string FuncCliWorkloadPackageType = "FuncCliWorkload";

    private readonly IWorkloadPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IGlobalManifestStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly INuspecReader _nuspecReader = nuspecReader ?? throw new ArgumentNullException(nameof(nuspecReader));
    private readonly IWorkloadEntryPointScanner _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));

    /// <inheritdoc />
    public async Task<InstalledWorkload> InstallFromDirectoryAsync(
        string packageDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);

        if (!Directory.Exists(packageDirectory))
        {
            throw new GracefulException(
                $"Package directory '{packageDirectory}' does not exist.",
                isUserError: true);
        }

        var nuspecPath = Directory
            .EnumerateFiles(packageDirectory, "*.nuspec", SearchOption.TopDirectoryOnly)
            .FirstOrDefault()
            ?? throw new GracefulException(
                $"No '.nuspec' found at the top level of '{packageDirectory}'.",
                isUserError: true);

        var meta = _nuspecReader.Read(nuspecPath);

        if (!meta.PackageTypes.Any(t => string.Equals(t, FuncCliWorkloadPackageType, StringComparison.OrdinalIgnoreCase)))
        {
            throw new GracefulException(
                $"Package '{meta.PackageId}' is not a func CLI workload " +
                $"(missing package type '{FuncCliWorkloadPackageType}' in its .nuspec).",
                isUserError: true);
        }

        var installPath = _paths.GetInstallDirectory(meta.PackageId, meta.Version);
        if (Directory.Exists(installPath))
        {
            throw new GracefulException(
                $"Workload '{meta.PackageId}' version '{meta.Version}' is already installed at '{installPath}'.",
                isUserError: true);
        }

        // Scan the staging directory before any disk mutation so a missing
        // or malformed entry point can't leave a half-installed workload
        // behind. EntryPointSpec.Assembly is a relative path, so the value
        // is identical whether scanned in staging or in the final location.
        var entryPoint = _scanner.Scan(packageDirectory);

        var parent = Path.GetDirectoryName(installPath)!;
        Directory.CreateDirectory(parent);
        Directory.Move(packageDirectory, installPath);

        var entry = new GlobalManifestEntry
        {
            DisplayName = string.IsNullOrEmpty(meta.Title) ? meta.PackageId : meta.Title,
            Description = meta.Description,
            Aliases = meta.Aliases,
            InstallPath = installPath,
            EntryPoint = entryPoint,
        };

        try
        {
            await _store.SaveWorkloadAsync(meta.PackageId, meta.Version, entry, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // Roll back the move so the next install attempt isn't blocked
            // by an orphaned directory the manifest doesn't know about.
            TryDeleteDirectory(installPath);
            throw;
        }

        return new InstalledWorkload(meta.PackageId, meta.Version, entry);
    }

    /// <inheritdoc />
    public async Task<bool> UninstallAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var removed = await _store.RemoveWorkloadAsync(packageId, version, cancellationToken)
            .ConfigureAwait(false);
        if (!removed)
        {
            return false;
        }

        var installPath = _paths.GetInstallDirectory(packageId, version);
        TryDeleteDirectory(installPath);
        return true;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort: if the install directory can't be removed (e.g.
            // an antivirus has the assembly open), the manifest is already
            // updated so a subsequent install for the same version will
            // surface a clear "already exists" error and the user can
            // remove the leftover directory manually.
        }
    }
}
