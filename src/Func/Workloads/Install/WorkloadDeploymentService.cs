// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Host;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Install;

internal sealed class WorkloadDeploymentService(IWorkloadPaths paths, IWorkloadStore store, IWorkloadMetadataReader metadataReader)
    : IWorkloadDeploymentService
{
    private readonly IWorkloadPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IWorkloadStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IWorkloadMetadataReader _metadataReader =
        metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));

    public async Task<WorkloadEntry> GetUpdateTargetAsync(string packageId, NuGetVersion? targetInstalledVersion,
        WorkloadOwnershipKind ownership, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);
        if (ownership == WorkloadOwnershipKind.Logical)
        {
            List<WorkloadEntry> matches = [.. installed.Where(entry =>
                entry.LogicalPackage is not null
                && string.Equals(entry.LogicalPackage.PackageId, packageId, StringComparison.OrdinalIgnoreCase))];
            if (matches.Count == 0)
            {
                throw new InvalidOperationException($"Logical workload '{packageId}' is not installed.");
            }

            return ResolveLogicalUpdateTarget(packageId, targetInstalledVersion, matches);
        }

        List<WorkloadEntry> explicitMatches = [.. installed.Where(entry =>
            entry.IsExplicitlyInstalled
            && string.Equals(entry.PackageId, packageId, StringComparison.OrdinalIgnoreCase))];
        if (explicitMatches.Count == 0)
        {
            throw new InvalidOperationException($"Explicit ownership for workload '{packageId}' is not installed.");
        }

        return ResolveUpdateTarget(packageId, targetInstalledVersion, explicitMatches);
    }

    public async Task<WorkloadInstallResult?> TryReuseExplicitAsync(string packageId, string version, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);
        WorkloadEntry? existing = installed.FirstOrDefault(entry =>
            string.Equals(entry.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.PackageVersion, version, StringComparison.Ordinal));
        if (existing is null
            || !existing.IsExplicitlyInstalled
            || !Directory.Exists(_paths.GetInstallDirectory(existing.PackageId, existing.PackageVersion)))
        {
            return null;
        }

        return new WorkloadInstallResult(existing, true);
    }

    public Task<WorkloadInstallResult?> TryReuseImplementationAsync(string packageId, string version, string runtimeIdentifier, LogicalPackage logicalPackage, CancellationToken cancellationToken = default)
        => TryReuseImplementationCoreAsync(packageId, version, runtimeIdentifier, logicalPackage, currentLogicalOwner: null, cancellationToken);

    public Task<WorkloadInstallResult?> TryReuseImplementationForUpdateAsync(WorkloadEntry currentEntry, string packageId, string version,
        string runtimeIdentifier, LogicalPackage logicalPackage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentEntry);
        return TryReuseImplementationCoreAsync(packageId, version, runtimeIdentifier, logicalPackage, currentEntry, cancellationToken);
    }

    public async Task<WorkloadInstallResult> InstallAsync(InspectedWorkloadPackage package, string source, WorkloadOwnershipKind ownership,
        LogicalPackage? logicalPackage, bool force, IProgress<WorkloadInstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        string packageId = package.Identity.PackageId;
        string version = package.Identity.Version;
        string installPath = _paths.GetInstallDirectory(packageId, version);
        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);
        WorkloadEntry? existing = installed.FirstOrDefault(entry =>
            string.Equals(entry.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.PackageVersion, version, StringComparison.Ordinal));
        WorkloadEntry? currentLogicalOwner = ResolveCurrentLogicalOwner(installed, ownership, logicalPackage);
        bool logicalOwnerMoves = LogicalOwnerMoves(currentLogicalOwner, packageId, version);

        if (!force && existing is not null && IsExistingInstallationValid(existing, package.Metadata))
        {
            return await AttachOwnershipAsync(existing, ownership, logicalPackage, currentLogicalOwner, cancellationToken);
        }

        string stagingPath = installPath + ".staging-" + Guid.NewGuid().ToString("N");
        bool movedToFinal = false;
        try
        {
            progress?.Report(new WorkloadInstallProgress(
                WorkloadInstallPhase.Extracting,
                $"Extracting workload '{packageId}' {version}"));
            Directory.CreateDirectory(stagingPath);
            using (PackageArchiveReader reader = OpenPackage(package.Path))
            {
                await ExtractPackageAsync(reader, stagingPath, cancellationToken);
            }

            WorkloadMetadata metadata = _metadataReader.Read(stagingPath);
            EnsureMetadataMatchesInspection(package.Metadata, metadata, packageId);
            EnsureHostExecutableBit(stagingPath, packageId, metadata.RuntimeIdentifier);
            WorkloadEntry incoming = CreateEntry(package, source, ownership, logicalPackage);
            WorkloadEntry entry = existing is null
                ? incoming
                : MergeForReinstall(existing, incoming, ownership);

            DeleteInstallDirectory(installPath);

            MoveDirectory(stagingPath, installPath);
            movedToFinal = true;
            progress?.Report(new WorkloadInstallProgress(
                WorkloadInstallPhase.Registering,
                $"Registering workload '{packageId}' {version}"));

            if (logicalOwnerMoves)
            {
                WorkloadOwnershipMoveResult moved =
                    await _store.MoveLogicalOwnershipAsync(currentLogicalOwner!.PackageId, currentLogicalOwner.PackageVersion, entry, cancellationToken);
                if (moved.PreviousEntryRemoved)
                {
                    TryDeleteDirectory(_paths.GetInstallDirectory(currentLogicalOwner.PackageId, currentLogicalOwner.PackageVersion));
                }

                return new WorkloadInstallResult(moved.Entry, false);
            }

            await _store.SaveWorkloadAsync(entry, cancellationToken);
            return new WorkloadInstallResult(entry, false);
        }
        catch
        {
            TryDeleteDirectory(stagingPath);
            if (movedToFinal && existing is null)
            {
                TryDeleteDirectory(installPath);
            }

            throw;
        }
    }

    public async Task<WorkloadEntry> UpdateAsync(WorkloadEntry currentEntry, InspectedWorkloadPackage package, string source,
        WorkloadOwnershipKind ownership, LogicalPackage? logicalPackage, IProgress<WorkloadInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentEntry);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (package.Role == WorkloadPackageRole.Pointer)
        {
            throw new InvalidWorkloadException(
                $"Pointer package '{package.Identity.PackageId}' cannot be installed as a physical workload.");
        }

        string finalPath = _paths.GetInstallDirectory(package.Identity.PackageId, package.Identity.Version);
        string stagingPath = finalPath + ".staging-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(stagingPath);
            using (PackageArchiveReader reader = OpenPackage(package.Path))
            {
                await ExtractPackageAsync(reader, stagingPath, cancellationToken);
            }

            WorkloadMetadata metadata = _metadataReader.Read(stagingPath);
            EnsureMetadataMatchesInspection(package.Metadata, metadata, package.Identity.PackageId);
            EnsureHostExecutableBit(stagingPath, package.Identity.PackageId, metadata.RuntimeIdentifier);
            WorkloadEntry incoming = CreateEntry(package, source, ownership, logicalPackage);

            DeleteInstallDirectory(finalPath);

            MoveDirectory(stagingPath, finalPath);
            WorkloadOwnershipMoveResult moved = ownership == WorkloadOwnershipKind.Logical
                ? await _store.MoveLogicalOwnershipAsync(currentEntry.PackageId, currentEntry.PackageVersion, incoming, cancellationToken)
                : await _store.MoveExplicitOwnershipAsync(currentEntry.PackageId, currentEntry.PackageVersion, incoming, cancellationToken);

            if (moved.PreviousEntryRemoved)
            {
                string oldPath = _paths.GetInstallDirectory(currentEntry.PackageId, currentEntry.PackageVersion);
                if (!string.Equals(oldPath, finalPath, StringComparison.Ordinal))
                {
                    TryDeleteDirectory(oldPath);
                }
            }

            return moved.Entry;
        }
        catch
        {
            TryDeleteDirectory(stagingPath);
            throw;
        }
    }

    public async Task<bool> UninstallAsync(string packageId, string version,
        WorkloadOwnershipKind ownership, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        string physicalPackageId = packageId;
        string physicalVersion = version;
        if (ownership == WorkloadOwnershipKind.Logical)
        {
            IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);
            WorkloadEntry[] logicalMatches = [.. installed.Where(entry =>
                entry.LogicalPackage is not null
                && string.Equals(entry.LogicalPackage.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.LogicalPackage.PackageVersion, version, StringComparison.Ordinal))];
            if (logicalMatches.Length == 0)
            {
                return false;
            }

            if (logicalMatches.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Logical workload '{packageId}' version '{version}' is attached to multiple physical packages.");
            }

            physicalPackageId = logicalMatches[0].PackageId;
            physicalVersion = logicalMatches[0].PackageVersion;
        }

        WorkloadOwnershipRemovalResult result = await _store.RemoveOwnershipAsync(physicalPackageId, physicalVersion, ownership, cancellationToken);
        if (!result.OwnershipRemoved)
        {
            return false;
        }

        if (result.EntryRemoved)
        {
            TryDeleteDirectory(_paths.GetInstallDirectory(physicalPackageId, physicalVersion));
        }

        return true;
    }

    private async Task<WorkloadInstallResult?> TryReuseImplementationCoreAsync(string packageId, string version, string runtimeIdentifier,
        LogicalPackage logicalPackage, WorkloadEntry? currentLogicalOwner, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentifier);
        ArgumentNullException.ThrowIfNull(logicalPackage);

        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);
        WorkloadEntry? existing = FindReusableImplementation(installed, packageId, version, runtimeIdentifier);
        if (existing is null)
        {
            return null;
        }

        currentLogicalOwner ??= ResolveCurrentLogicalOwner(installed, WorkloadOwnershipKind.Logical, logicalPackage);
        return await AttachOwnershipAsync(existing, WorkloadOwnershipKind.Logical, logicalPackage, currentLogicalOwner, cancellationToken);
    }

    private async Task<WorkloadInstallResult> AttachOwnershipAsync(WorkloadEntry existing, WorkloadOwnershipKind ownership,
        LogicalPackage? logicalPackage, WorkloadEntry? currentLogicalOwner, CancellationToken cancellationToken)
    {
        if (LogicalOwnerMoves(currentLogicalOwner, existing.PackageId, existing.PackageVersion))
        {
            WorkloadEntry incoming = CopyForOwnership(existing, WorkloadOwnershipKind.Logical, logicalPackage);
            WorkloadOwnershipMoveResult moved =
                await _store.MoveLogicalOwnershipAsync(currentLogicalOwner!.PackageId, currentLogicalOwner.PackageVersion, incoming, cancellationToken);
            if (moved.PreviousEntryRemoved)
            {
                TryDeleteDirectory(_paths.GetInstallDirectory(currentLogicalOwner.PackageId, currentLogicalOwner.PackageVersion));
            }

            return new WorkloadInstallResult(moved.Entry, false);
        }

        if (HasOwnership(existing, ownership))
        {
            return new WorkloadInstallResult(existing, true);
        }

        WorkloadEntry attached =
            await _store.AddOwnershipAsync(CopyForOwnership(existing, ownership, logicalPackage), ownership, cancellationToken);
        return new WorkloadInstallResult(attached, false);
    }

    private static WorkloadEntry? ResolveCurrentLogicalOwner(IReadOnlyList<WorkloadEntry> installed, WorkloadOwnershipKind ownership,
        LogicalPackage? logicalPackage)
    {
        if (ownership != WorkloadOwnershipKind.Logical || logicalPackage is null)
        {
            return null;
        }

        WorkloadEntry[] logicalOwners = [.. installed.Where(entry =>
            entry.LogicalPackage is not null
            && string.Equals(entry.LogicalPackage.PackageId, logicalPackage.PackageId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.LogicalPackage.PackageVersion, logicalPackage.PackageVersion, StringComparison.Ordinal))];
        if (logicalOwners.Length > 1)
        {
            throw new InvalidOperationException(
                $"Logical workload '{logicalPackage.PackageId}' version '{logicalPackage.PackageVersion}' " +
                "is attached to multiple physical packages.");
        }

        return logicalOwners.FirstOrDefault();
    }

    private static bool LogicalOwnerMoves(WorkloadEntry? currentLogicalOwner, string packageId, string version)
        => currentLogicalOwner is not null
            && (!string.Equals(currentLogicalOwner.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(currentLogicalOwner.PackageVersion, version, StringComparison.Ordinal));

    private WorkloadEntry? FindReusableImplementation(IReadOnlyList<WorkloadEntry> installed, string packageId, string version,
        string runtimeIdentifier)
    {
        WorkloadEntry? existing = installed.FirstOrDefault(entry =>
            string.Equals(entry.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.PackageVersion, version, StringComparison.Ordinal)
            && string.Equals(entry.RuntimeIdentifier, runtimeIdentifier, StringComparison.Ordinal));
        if (existing is null)
        {
            return null;
        }

        string installPath = _paths.GetInstallDirectory(existing.PackageId, existing.PackageVersion);
        if (!Directory.Exists(installPath))
        {
            return null;
        }

        try
        {
            WorkloadMetadata metadata = _metadataReader.Read(installPath);
            return metadata.Kind is WorkloadKind.Workload or WorkloadKind.Content
                && string.Equals(metadata.RuntimeIdentifier, runtimeIdentifier, StringComparison.Ordinal)
                ? existing
                : null;
        }
        catch (InvalidWorkloadException)
        {
            return null;
        }
    }

    private static WorkloadEntry CreateEntry(InspectedWorkloadPackage package, string source, WorkloadOwnershipKind ownership, LogicalPackage? logicalPackage)
        => new()
        {
            PackageId = package.Identity.PackageId,
            PackageVersion = package.Identity.Version,
            Aliases = package.Identity.Aliases,
            DisplayName = GetDisplayName(package.Metadata, package.Identity.Title, package.Identity.PackageId),
            Description = GetDescription(package.Metadata, package.Identity.Description),
            EntryPoint = package.Metadata.EntryPoint,
            Kind = package.Metadata.Kind,
            Source = source,
            RuntimeIdentifier = package.Metadata.RuntimeIdentifier,
            IsExplicitlyInstalled = ownership == WorkloadOwnershipKind.Explicit,
            LogicalPackage = logicalPackage,
            InstallRefCount = 1,
        };

    private static WorkloadEntry CopyForOwnership(WorkloadEntry entry, WorkloadOwnershipKind ownership, LogicalPackage? logicalPackage)
        => new()
        {
            PackageId = entry.PackageId,
            PackageVersion = entry.PackageVersion,
            Aliases = entry.Aliases,
            DisplayName = entry.DisplayName,
            Description = entry.Description,
            EntryPoint = entry.EntryPoint,
            Kind = entry.Kind,
            Source = entry.Source,
            RuntimeIdentifier = entry.RuntimeIdentifier,
            IsExplicitlyInstalled = ownership == WorkloadOwnershipKind.Explicit || entry.IsExplicitlyInstalled,
            LogicalPackage = entry.LogicalPackage ?? logicalPackage,
            InstallRefCount = entry.InstallRefCount,
        };

    private static WorkloadEntry MergeForReinstall(WorkloadEntry existing, WorkloadEntry incoming, WorkloadOwnershipKind ownership)
    {
        bool addExplicit = ownership == WorkloadOwnershipKind.Explicit && !existing.IsExplicitlyInstalled;
        bool addLogical = ownership == WorkloadOwnershipKind.Logical && existing.LogicalPackage is null;
        return new WorkloadEntry
        {
            PackageId = incoming.PackageId,
            PackageVersion = incoming.PackageVersion,
            Aliases = incoming.Aliases,
            DisplayName = incoming.DisplayName,
            Description = incoming.Description,
            EntryPoint = incoming.EntryPoint,
            Kind = incoming.Kind,
            Source = incoming.Source,
            RuntimeIdentifier = incoming.RuntimeIdentifier,
            IsExplicitlyInstalled = existing.IsExplicitlyInstalled || incoming.IsExplicitlyInstalled,
            LogicalPackage = existing.LogicalPackage ?? incoming.LogicalPackage,
            InstallRefCount = existing.InstallRefCount + (addExplicit ? 1 : 0) + (addLogical ? 1 : 0),
        };
    }

    private bool IsExistingInstallationValid(WorkloadEntry entry, WorkloadMetadata expectedMetadata)
    {
        string installPath = _paths.GetInstallDirectory(entry.PackageId, entry.PackageVersion);
        if (!Directory.Exists(installPath))
        {
            return false;
        }

        try
        {
            WorkloadMetadata actual = _metadataReader.Read(installPath);
            return actual.Kind == expectedMetadata.Kind
                && string.Equals(actual.RuntimeIdentifier, expectedMetadata.RuntimeIdentifier, StringComparison.Ordinal);
        }
        catch (InvalidWorkloadException)
        {
            return false;
        }
    }

    private static bool HasOwnership(WorkloadEntry entry, WorkloadOwnershipKind ownership)
        => ownership == WorkloadOwnershipKind.Explicit
            ? entry.IsExplicitlyInstalled
            : entry.LogicalPackage is not null;

    private static WorkloadEntry ResolveUpdateTarget(string packageId, NuGetVersion? targetInstalledVersion,
        IReadOnlyList<WorkloadEntry> matches)
    {
        if (targetInstalledVersion is null)
        {
            return matches.OrderByDescending(entry => NuGetVersion.Parse(entry.PackageVersion)).First();
        }

        string requested = targetInstalledVersion.ToNormalizedString();
        WorkloadEntry? exact = matches.FirstOrDefault(entry =>
            string.Equals(entry.PackageVersion, requested, StringComparison.Ordinal));
        if (exact is null)
        {
            string available = string.Join(", ", matches.Select(match => match.PackageVersion));
            throw new InvalidOperationException(
                $"Workload '{packageId}' version '{requested}' is not installed. Installed versions: {available}.");
        }

        return exact;
    }

    private static WorkloadEntry ResolveLogicalUpdateTarget(string packageId, NuGetVersion? targetInstalledVersion,
        IReadOnlyList<WorkloadEntry> matches)
    {
        if (targetInstalledVersion is null)
        {
            return matches
                .OrderByDescending(entry => NuGetVersion.Parse(entry.LogicalPackage!.PackageVersion))
                .First();
        }

        string requested = targetInstalledVersion.ToNormalizedString();
        WorkloadEntry? exact = matches.FirstOrDefault(entry =>
            string.Equals(entry.LogicalPackage!.PackageVersion, requested, StringComparison.Ordinal));
        if (exact is null)
        {
            string available = string.Join(", ", matches.Select(entry => entry.LogicalPackage!.PackageVersion));
            throw new InvalidOperationException(
                $"Logical workload '{packageId}' version '{requested}' is not installed. Installed versions: {available}.");
        }

        return exact;
    }

    private static void EnsureMetadataMatchesInspection(WorkloadMetadata expected, WorkloadMetadata actual, string packageId)
    {
        if (expected.Kind != actual.Kind
            || !string.Equals(expected.RuntimeIdentifier, actual.RuntimeIdentifier, StringComparison.Ordinal))
        {
            throw new InvalidWorkloadException(
                $"Package '{packageId}' workload.json changed while the package was being installed.");
        }
    }

    private static PackageArchiveReader OpenPackage(string nupkgPath)
    {
        Stream stream = File.OpenRead(nupkgPath);
        try
        {
            return new PackageArchiveReader(stream);
        }
        catch (Exception ex) when (ex is InvalidDataException or PackagingException)
        {
            stream.Dispose();
            throw new InvalidWorkloadException(
                $"Failed to read .nupkg at '{nupkgPath}': {ex.Message}",
                ex);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static Task ExtractPackageAsync(PackageArchiveReader reader, string destination, CancellationToken cancellationToken)
        => ExtractPackageFilesAsync(reader, destination, IsInstallablePackageFile, cancellationToken);

    private static async Task ExtractPackageFilesAsync(PackageArchiveReader reader, string destination, Func<string, bool> include,
        CancellationToken cancellationToken)
    {
        foreach (string packageFile in await reader.GetFilesAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!include(packageFile))
            {
                continue;
            }

            string targetPath = Path.Combine(destination, packageFile.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            using Stream entryStream = await reader.GetStreamAsync(packageFile, cancellationToken);
            using FileStream output = File.Create(targetPath);
            await entryStream.CopyToAsync(output, cancellationToken);
        }
    }

    private static bool IsMetadataFile(string packageFile)
        => string.Equals(packageFile, WorkloadMetadataReader.MetadataFileName, StringComparison.OrdinalIgnoreCase);

    private static bool IsInstallablePackageFile(string packageFile)
        => IsMetadataFile(packageFile) || IsPayloadFile(packageFile);

    private static bool IsPayloadFile(string packageFile)
        => packageFile.StartsWith("tools/", StringComparison.OrdinalIgnoreCase);

    private static void EnsureHostExecutableBit(string installPath, string packageId, string? runtimeIdentifier)
    {
        if (OperatingSystem.IsWindows()
            || !packageId.StartsWith(HostWorkloadPackage.PackageIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string hostBinary = Path.Combine(
            WorkloadPackageLayout.GetContentRoot(installPath, runtimeIdentifier), HostProcessStartInfoFactory.ExecutableBaseName);
        if (!File.Exists(hostBinary))
        {
            return;
        }

        UnixFileMode mode = File.GetUnixFileMode(hostBinary);
        File.SetUnixFileMode(hostBinary, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    private static string GetDisplayName(WorkloadMetadata metadata, string? nuspecTitle, string packageId)
        => !string.IsNullOrWhiteSpace(metadata.DisplayName)
            ? metadata.DisplayName
            : string.IsNullOrWhiteSpace(nuspecTitle) ? packageId : nuspecTitle;

    private static string GetDescription(WorkloadMetadata metadata, string? nuspecDescription)
        => !string.IsNullOrWhiteSpace(metadata.Description)
            ? metadata.Description
            : string.IsNullOrWhiteSpace(nuspecDescription) ? string.Empty : nuspecDescription;

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
            // Best-effort cleanup leaves registry ownership intact.
        }
    }

    private static void DeleteInstallDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Directory.Delete(path, recursive: true);

        // Windows can report a successful delete while the directory lingers until the last handle closes.
        if (Directory.Exists(path))
        {
            throw new IOException($"Failed to remove the existing install directory '{path}'.");
        }
    }

    private static void MoveDirectory(string source, string destination)
    {
        try
        {
            Directory.Move(source, destination);
        }
        catch (IOException)
        {
            Directory.CreateDirectory(destination);
            foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
            }

            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(file, target, overwrite: true);
            }

            Directory.Delete(source, recursive: true);
        }
    }
}
