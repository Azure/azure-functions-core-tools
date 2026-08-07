// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Host;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Install;

internal sealed class WorkloadInstaller(
    IWorkloadPaths paths,
    IWorkloadStore store,
    IWorkloadMetadataReader metadataReader,
    IWorkloadCatalog catalog,
    IWorkloadRuntimeIdentifierProvider runtimeIdentifierProvider,
    IOptions<WorkloadCatalogOptions> catalogOptions) : IWorkloadInstaller
{
    public const string AliasTagPrefix = "alias:";
    public const string KindTagPrefix = "kind:";
    public const string RidTagPrefix = "rid:";
    public const string FuncCliWorkloadPackageType = "FuncCliWorkload";
    public const string FuncCliWorkloadRidPackageType = "FuncCliWorkloadRidPackage";

    private readonly IWorkloadPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IWorkloadStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IWorkloadMetadataReader _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
    private readonly IWorkloadCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly IWorkloadRuntimeIdentifierProvider _runtimeIdentifierProvider =
        runtimeIdentifierProvider ?? throw new ArgumentNullException(nameof(runtimeIdentifierProvider));
    private readonly WorkloadCatalogOptions _catalogOptions = catalogOptions?.Value ?? throw new ArgumentNullException(nameof(catalogOptions));

    internal WorkloadInstaller(
        IWorkloadPaths paths,
        IWorkloadStore store,
        IWorkloadMetadataReader metadataReader,
        IWorkloadCatalog catalog,
        IOptions<WorkloadCatalogOptions> catalogOptions)
        : this(paths, store, metadataReader, catalog, new WorkloadRuntimeIdentifierProvider(), catalogOptions)
    {
    }

    public async Task<WorkloadInstallResult> InstallFromPackageAsync(
        string nupkgPath,
        bool force = false,
        IProgress<WorkloadInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nupkgPath);
        if (!File.Exists(nupkgPath))
        {
            throw new FileNotFoundException($"Package file '{nupkgPath}' does not exist.", nupkgPath);
        }

        string fullPath = Path.GetFullPath(nupkgPath);
        InspectedPackage package = await InspectPackageAsync(fullPath, cancellationToken);
        if (package.Role != PackageRole.Pointer)
        {
            return await InstallPayloadAsync(
                package,
                fullPath,
                WorkloadOwnershipKind.Explicit,
                logicalPackage: null,
                force,
                progress,
                cancellationToken);
        }

        PointerSelection selection = SelectPointerImplementation(package);
        LogicalPackage logicalPackage = CreateLogicalPackage(package, fullPath);
        string implementationPath = FindLocalImplementation(
            fullPath,
            selection.PackageId,
            package.Identity.Version,
            selection.RuntimeIdentifier);
        InspectedPackage implementation = await InspectPackageAsync(implementationPath, cancellationToken);
        ValidatePointerImplementation(package, selection, implementation);

        return await InstallPayloadAsync(
            implementation,
            implementationPath,
            WorkloadOwnershipKind.Logical,
            logicalPackage,
            force,
            progress,
            cancellationToken);
    }

    public async Task<WorkloadInstallResult> InstallFromCatalogAsync(
        string packageId,
        NuGetVersion? version,
        string? source,
        bool? includePrerelease,
        bool exact,
        bool force,
        IProgress<WorkloadInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        bool effectiveIncludePrerelease = IncludePrerelease(includePrerelease);
        progress?.Report(new WorkloadInstallProgress(WorkloadInstallPhase.Resolving, $"Resolving workload '{packageId}'"));

        string resolvedId = exact
            ? packageId
            : await ResolveAliasOrIdAsync(packageId, source, effectiveIncludePrerelease, cancellationToken);
        ResolvedPackage resolved = await ResolveCatalogPackageAsync(
            resolvedId,
            version,
            source,
            effectiveIncludePrerelease,
            cancellationToken);

        WorkloadInstallResult? reused = await TryReuseResolvedPackageAsync(resolved, force, cancellationToken);
        if (reused is not null)
        {
            return reused;
        }

        string packagePath = await DownloadToTempAsync(resolved, progress, cancellationToken);
        try
        {
            InspectedPackage package = await InspectPackageAsync(packagePath, cancellationToken);
            ValidateResolvedIdentity(resolved, package.Identity);
            if (package.Role != PackageRole.Pointer)
            {
                return await InstallPayloadAsync(
                    package,
                    resolved.Source.Source,
                    WorkloadOwnershipKind.Explicit,
                    logicalPackage: null,
                    force,
                    progress,
                    cancellationToken);
            }

            PointerSelection selection = SelectPointerImplementation(package);
            LogicalPackage logicalPackage = CreateLogicalPackage(package, resolved.Source.Source);
            WorkloadInstallResult? existingImplementation = !force
                ? await TryReuseInstalledImplementationAsync(
                    selection.PackageId,
                    package.Identity.Version,
                    selection.RuntimeIdentifier,
                    WorkloadOwnershipKind.Logical,
                    logicalPackage,
                    cancellationToken)
                : null;
            if (existingImplementation is not null)
            {
                return existingImplementation;
            }

            ResolvedPackage implementationResolved = await ResolvePointerImplementationAsync(
                package,
                selection,
                resolved.Source.Source,
                cancellationToken);
            string implementationPath = await DownloadToTempAsync(implementationResolved, progress, cancellationToken);
            try
            {
                InspectedPackage implementation = await InspectPackageAsync(implementationPath, cancellationToken);
                ValidateResolvedIdentity(implementationResolved, implementation.Identity);
                ValidatePointerImplementation(package, selection, implementation);
                return await InstallPayloadAsync(
                    implementation,
                    implementationResolved.Source.Source,
                    WorkloadOwnershipKind.Logical,
                    logicalPackage,
                    force,
                    progress,
                    cancellationToken);
            }
            finally
            {
                TryDeleteFile(implementationPath);
            }
        }
        finally
        {
            TryDeleteFile(packagePath);
        }
    }

    public async Task<WorkloadUpdateResult> UpdateAsync(
        string packageId,
        NuGetVersion? targetInstalledVersion,
        string? source,
        bool? includePrerelease,
        bool allowMajor,
        IProgress<WorkloadInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => await UpdateAsync(
            packageId,
            targetInstalledVersion,
            source,
            includePrerelease,
            allowMajor,
            WorkloadOwnershipKind.Explicit,
            progress,
            cancellationToken);

    public async Task<WorkloadUpdateResult> UpdateAsync(
        string packageId,
        NuGetVersion? targetInstalledVersion,
        string? source,
        bool? includePrerelease,
        bool allowMajor,
        WorkloadOwnershipKind ownership,
        IProgress<WorkloadInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        return ownership == WorkloadOwnershipKind.Logical
            ? await UpdateLogicalAsync(
                packageId,
                targetInstalledVersion,
                source,
                includePrerelease,
                allowMajor,
                progress,
                cancellationToken)
            : await UpdateExplicitAsync(
                packageId,
                targetInstalledVersion,
                source,
                includePrerelease,
                allowMajor,
                progress,
                cancellationToken);
    }

    public async Task<bool> UninstallAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default)
        => await UninstallAsync(
            packageId,
            version,
            WorkloadOwnershipKind.Explicit,
            cancellationToken);

    public async Task<bool> UninstallAsync(
        string packageId,
        string version,
        WorkloadOwnershipKind ownership,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        string physicalPackageId = packageId;
        string physicalVersion = version;
        if (ownership == WorkloadOwnershipKind.Logical)
        {
            IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);
            WorkloadEntry[] logicalMatches = [.. installed.Where(e =>
                e.LogicalPackage is not null
                && string.Equals(e.LogicalPackage.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.LogicalPackage.PackageVersion, version, StringComparison.Ordinal))];
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

        WorkloadOwnershipRemovalResult result = await _store.RemoveOwnershipAsync(
            physicalPackageId,
            physicalVersion,
            ownership,
            cancellationToken);
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

    private async Task<WorkloadUpdateResult> UpdateExplicitAsync(
        string packageId,
        NuGetVersion? targetInstalledVersion,
        string? source,
        bool? includePrerelease,
        bool allowMajor,
        IProgress<WorkloadInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        bool effectiveIncludePrerelease = IncludePrerelease(includePrerelease);
        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);
        List<WorkloadEntry> matches = [.. installed.Where(e =>
            e.IsExplicitlyInstalled
            && string.Equals(e.PackageId, packageId, StringComparison.OrdinalIgnoreCase))];
        if (matches.Count == 0)
        {
            throw new InvalidOperationException($"Explicit ownership for workload '{packageId}' is not installed.");
        }

        WorkloadEntry currentEntry = ResolveUpdateTarget(packageId, targetInstalledVersion, matches);
        var currentVersion = NuGetVersion.Parse(currentEntry.PackageVersion);
        ResolvedPackage? resolved = await _catalog.ResolveLatestVersionAsync(
            currentEntry.PackageId,
            effectiveIncludePrerelease,
            currentVersion,
            allowMajor,
            source,
            cancellationToken);
        if (resolved is null)
        {
            return new WorkloadUpdateResult(currentEntry, currentEntry.PackageVersion, true, true);
        }

        if (resolved.Version <= currentVersion)
        {
            return new WorkloadUpdateResult(currentEntry, currentEntry.PackageVersion, true);
        }

        WorkloadEntry newEntry = await StagePhysicalUpdateAsync(
            currentEntry,
            resolved,
            WorkloadOwnershipKind.Explicit,
            logicalPackage: null,
            progress,
            cancellationToken);
        return new WorkloadUpdateResult(newEntry, currentEntry.PackageVersion, false);
    }

    private async Task<WorkloadUpdateResult> UpdateLogicalAsync(
        string packageId,
        NuGetVersion? targetInstalledVersion,
        string? source,
        bool? includePrerelease,
        bool allowMajor,
        IProgress<WorkloadInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        bool effectiveIncludePrerelease = IncludePrerelease(includePrerelease);
        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);
        List<WorkloadEntry> matches = [.. installed.Where(e =>
            e.LogicalPackage is not null
            && string.Equals(e.LogicalPackage.PackageId, packageId, StringComparison.OrdinalIgnoreCase))];
        if (matches.Count == 0)
        {
            throw new InvalidOperationException($"Logical workload '{packageId}' is not installed.");
        }

        WorkloadEntry currentEntry = ResolveLogicalUpdateTarget(packageId, targetInstalledVersion, matches);
        LogicalPackage currentLogical = currentEntry.LogicalPackage!;
        if (source is null && IsLocalPackageSource(currentLogical.Source))
        {
            throw new InvalidOperationException(
                $"Logical workload '{currentLogical.PackageId}' was installed from a local package and has no automatic update source. " +
                "Install a newer local pointer package explicitly or pass --source.");
        }

        var currentVersion = NuGetVersion.Parse(currentLogical.PackageVersion);
        string? effectiveSource = source ?? (string.IsNullOrWhiteSpace(currentLogical.Source) ? null : currentLogical.Source);
        ResolvedPackage? pointerResolved = await _catalog.ResolveLatestVersionAsync(
            currentLogical.PackageId,
            effectiveIncludePrerelease,
            currentVersion,
            allowMajor,
            effectiveSource,
            cancellationToken);
        if (pointerResolved is null)
        {
            return new WorkloadUpdateResult(currentEntry, currentLogical.PackageVersion, true, true);
        }

        if (pointerResolved.Version < currentVersion)
        {
            return new WorkloadUpdateResult(currentEntry, currentLogical.PackageVersion, true);
        }

        string pointerPath = await DownloadToTempAsync(pointerResolved, progress, cancellationToken);
        try
        {
            InspectedPackage pointer = await InspectPackageAsync(pointerPath, cancellationToken);
            ValidateResolvedIdentity(pointerResolved, pointer.Identity);
            if (pointer.Role != PackageRole.Pointer)
            {
                throw new InvalidWorkloadException(
                    $"Package '{pointer.Identity.PackageId}' {pointer.Identity.Version} is not a rid-pointer workload.");
            }

            PointerSelection selection = SelectPointerImplementation(pointer);
            bool ridPivot = !string.Equals(
                currentEntry.RuntimeIdentifier,
                selection.RuntimeIdentifier,
                StringComparison.Ordinal);
            if (pointerResolved.Version == currentVersion && !ridPivot)
            {
                return new WorkloadUpdateResult(currentEntry, currentLogical.PackageVersion, true);
            }

            LogicalPackage newLogical = CreateLogicalPackage(pointer, pointerResolved.Source.Source);
            WorkloadEntry? reusable = FindReusableImplementation(
                installed,
                selection.PackageId,
                pointer.Identity.Version,
                selection.RuntimeIdentifier);
            if (reusable is not null)
            {
                WorkloadEntry incoming = CopyForOwnership(reusable, WorkloadOwnershipKind.Logical, newLogical);
                WorkloadOwnershipMoveResult moved = await _store.MoveLogicalOwnershipAsync(
                    currentEntry.PackageId,
                    currentEntry.PackageVersion,
                    incoming,
                    cancellationToken);
                if (moved.PreviousEntryRemoved)
                {
                    TryDeleteDirectory(_paths.GetInstallDirectory(currentEntry.PackageId, currentEntry.PackageVersion));
                }

                return new WorkloadUpdateResult(moved.Entry, currentLogical.PackageVersion, false);
            }

            ResolvedPackage implementationResolved = await ResolvePointerImplementationAsync(
                pointer,
                selection,
                pointerResolved.Source.Source,
                cancellationToken);
            string implementationPath = await DownloadToTempAsync(implementationResolved, progress, cancellationToken);
            try
            {
                InspectedPackage implementation = await InspectPackageAsync(implementationPath, cancellationToken);
                ValidateResolvedIdentity(implementationResolved, implementation.Identity);
                ValidatePointerImplementation(pointer, selection, implementation);
                WorkloadEntry newEntry = await StagePhysicalUpdateAsync(
                    currentEntry,
                    implementationResolved,
                    WorkloadOwnershipKind.Logical,
                    newLogical,
                    progress,
                    cancellationToken,
                    implementationPath,
                    implementation);
                return new WorkloadUpdateResult(newEntry, currentLogical.PackageVersion, false);
            }
            finally
            {
                TryDeleteFile(implementationPath);
            }
        }
        finally
        {
            TryDeleteFile(pointerPath);
        }
    }

    private async Task<WorkloadEntry> StagePhysicalUpdateAsync(
        WorkloadEntry currentEntry,
        ResolvedPackage resolved,
        WorkloadOwnershipKind ownership,
        LogicalPackage? logicalPackage,
        IProgress<WorkloadInstallProgress>? progress,
        CancellationToken cancellationToken,
        string? downloadedPath = null,
        InspectedPackage? inspectedPackage = null)
    {
        string version = resolved.Version.ToNormalizedString();
        bool ownsDownload = downloadedPath is null;
        string packagePath = downloadedPath ?? await DownloadToTempAsync(resolved, progress, cancellationToken);
        string finalPath = _paths.GetInstallDirectory(resolved.PackageId, version);
        string stagingPath = finalPath + ".staging-" + Guid.NewGuid().ToString("N");

        try
        {
            InspectedPackage package = inspectedPackage ?? await InspectPackageAsync(packagePath, cancellationToken);
            ValidateResolvedIdentity(resolved, package.Identity);
            if (package.Role == PackageRole.Pointer)
            {
                throw new InvalidWorkloadException(
                    $"Pointer package '{package.Identity.PackageId}' cannot be installed as a physical workload.");
            }

            Directory.CreateDirectory(stagingPath);
            using (PackageArchiveReader reader = OpenPackage(packagePath))
            {
                await ExtractPackageAsync(reader, stagingPath, cancellationToken);
            }

            WorkloadMetadata metadata = _metadataReader.Read(stagingPath);
            EnsureMetadataMatchesInspection(package.Metadata, metadata, package.Identity.PackageId);
            EnsureHostExecutableBit(stagingPath, package.Identity.PackageId, metadata.RuntimeIdentifier);
            WorkloadEntry incoming = CreateEntry(package, resolved.Source.Source, ownership, logicalPackage);

            DeleteInstallDirectory(finalPath);

            MoveDirectory(stagingPath, finalPath);
            WorkloadOwnershipMoveResult moved = ownership == WorkloadOwnershipKind.Logical
                ? await _store.MoveLogicalOwnershipAsync(
                    currentEntry.PackageId,
                    currentEntry.PackageVersion,
                    incoming,
                    cancellationToken)
                : await _store.MoveExplicitOwnershipAsync(
                    currentEntry.PackageId,
                    currentEntry.PackageVersion,
                    incoming,
                    cancellationToken);

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
        finally
        {
            if (ownsDownload)
            {
                TryDeleteFile(packagePath);
            }
        }
    }

    private async Task<WorkloadInstallResult> InstallPayloadAsync(
        InspectedPackage package,
        string source,
        WorkloadOwnershipKind ownership,
        LogicalPackage? logicalPackage,
        bool force,
        IProgress<WorkloadInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        string packageId = package.Identity.PackageId;
        string version = package.Identity.Version;
        string installPath = _paths.GetInstallDirectory(packageId, version);
        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);
        WorkloadEntry? existing = installed.FirstOrDefault(e =>
            string.Equals(e.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.PackageVersion, version, StringComparison.Ordinal));
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
                WorkloadOwnershipMoveResult moved = await _store.MoveLogicalOwnershipAsync(
                    currentLogicalOwner!.PackageId,
                    currentLogicalOwner.PackageVersion,
                    entry,
                    cancellationToken);
                if (moved.PreviousEntryRemoved)
                {
                    TryDeleteDirectory(_paths.GetInstallDirectory(
                        currentLogicalOwner.PackageId,
                        currentLogicalOwner.PackageVersion));
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

    private async Task<WorkloadInstallResult?> TryReuseResolvedPackageAsync(
        ResolvedPackage resolved,
        bool force,
        CancellationToken cancellationToken)
    {
        if (force)
        {
            return null;
        }

        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);
        WorkloadEntry? existing = installed.FirstOrDefault(e =>
            string.Equals(e.PackageId, resolved.PackageId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.PackageVersion, resolved.Version.ToNormalizedString(), StringComparison.Ordinal));
        if (existing is null || !existing.IsExplicitlyInstalled || !Directory.Exists(
            _paths.GetInstallDirectory(existing.PackageId, existing.PackageVersion)))
        {
            return null;
        }

        return new WorkloadInstallResult(existing, true);
    }

    private async Task<WorkloadInstallResult?> TryReuseInstalledImplementationAsync(
        string packageId,
        string version,
        string runtimeIdentifier,
        WorkloadOwnershipKind ownership,
        LogicalPackage logicalPackage,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);
        WorkloadEntry? existing = FindReusableImplementation(installed, packageId, version, runtimeIdentifier);
        if (existing is null)
        {
            return null;
        }

        WorkloadEntry? currentLogicalOwner = ResolveCurrentLogicalOwner(installed, ownership, logicalPackage);
        return await AttachOwnershipAsync(existing, ownership, logicalPackage, currentLogicalOwner, cancellationToken);
    }

    /// <summary>
    /// Attaches the requested ownership to an already-installed payload, moving an existing logical
    /// owner off its previous physical package when the pointer now resolves elsewhere.
    /// </summary>
    private async Task<WorkloadInstallResult> AttachOwnershipAsync(
        WorkloadEntry existing,
        WorkloadOwnershipKind ownership,
        LogicalPackage? logicalPackage,
        WorkloadEntry? currentLogicalOwner,
        CancellationToken cancellationToken)
    {
        if (LogicalOwnerMoves(currentLogicalOwner, existing.PackageId, existing.PackageVersion))
        {
            WorkloadEntry incoming = CopyForOwnership(existing, WorkloadOwnershipKind.Logical, logicalPackage);
            WorkloadOwnershipMoveResult moved = await _store.MoveLogicalOwnershipAsync(
                currentLogicalOwner!.PackageId,
                currentLogicalOwner.PackageVersion,
                incoming,
                cancellationToken);
            if (moved.PreviousEntryRemoved)
            {
                TryDeleteDirectory(_paths.GetInstallDirectory(
                    currentLogicalOwner.PackageId,
                    currentLogicalOwner.PackageVersion));
            }

            return new WorkloadInstallResult(moved.Entry, false);
        }

        if (HasOwnership(existing, ownership))
        {
            return new WorkloadInstallResult(existing, true);
        }

        WorkloadEntry attached = await _store.AddOwnershipAsync(
            CopyForOwnership(existing, ownership, logicalPackage),
            ownership,
            cancellationToken);
        return new WorkloadInstallResult(attached, false);
    }

    /// <summary>
    /// Finds the physical package that currently owns the given logical pointer, if any.
    /// </summary>
    private static WorkloadEntry? ResolveCurrentLogicalOwner(
        IReadOnlyList<WorkloadEntry> installed,
        WorkloadOwnershipKind ownership,
        LogicalPackage? logicalPackage)
    {
        if (ownership != WorkloadOwnershipKind.Logical || logicalPackage is null)
        {
            return null;
        }

        WorkloadEntry[] logicalOwners = [.. installed.Where(e =>
            e.LogicalPackage is not null
            && string.Equals(e.LogicalPackage.PackageId, logicalPackage.PackageId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.LogicalPackage.PackageVersion, logicalPackage.PackageVersion, StringComparison.Ordinal))];
        if (logicalOwners.Length > 1)
        {
            throw new InvalidOperationException(
                $"Logical workload '{logicalPackage.PackageId}' version '{logicalPackage.PackageVersion}' is attached to multiple physical packages.");
        }

        return logicalOwners.FirstOrDefault();
    }

    private static bool LogicalOwnerMoves(WorkloadEntry? currentLogicalOwner, string packageId, string version)
        => currentLogicalOwner is not null
            && (!string.Equals(currentLogicalOwner.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(currentLogicalOwner.PackageVersion, version, StringComparison.Ordinal));

    private WorkloadEntry? FindReusableImplementation(
        IReadOnlyList<WorkloadEntry> installed,
        string packageId,
        string version,
        string runtimeIdentifier)
    {
        WorkloadEntry? existing = installed.FirstOrDefault(e =>
            string.Equals(e.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.PackageVersion, version, StringComparison.Ordinal)
            && string.Equals(e.RuntimeIdentifier, runtimeIdentifier, StringComparison.Ordinal));
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

    private async Task<InspectedPackage> InspectPackageAsync(string path, CancellationToken cancellationToken)
    {
        using PackageArchiveReader reader = OpenPackage(path);
        NuspecReader nuspec = reader.NuspecReader;
        PackageIdentity identity = new(
            nuspec.GetId().ToLowerInvariant(),
            nuspec.GetVersion().ToNormalizedString(),
            [.. nuspec.GetPackageTypes().Select(t => t.Name)],
            ParseAliases(nuspec.GetTags()),
            ParseTagValues(nuspec.GetTags(), RidTagPrefix),
            nuspec.GetTitle(),
            nuspec.GetDescription());

        string inspectionDirectory = Path.Combine(Path.GetTempPath(), $"func-workload-inspect-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(inspectionDirectory);
            await ExtractMetadataOnlyAsync(reader, inspectionDirectory, cancellationToken);
            WorkloadMetadata metadata = _metadataReader.Read(inspectionDirectory);
            IReadOnlyList<string> files = [.. await reader.GetFilesAsync(cancellationToken)];
            PackageRole role = ValidatePackage(identity, metadata, files);
            return new InspectedPackage(path, identity, metadata, role);
        }
        finally
        {
            TryDeleteDirectory(inspectionDirectory);
        }
    }

    private PackageRole ValidatePackage(
        PackageIdentity identity,
        WorkloadMetadata metadata,
        IReadOnlyList<string> files)
    {
        bool standardType = identity.PackageTypes.Any(t =>
            string.Equals(t, FuncCliWorkloadPackageType, StringComparison.OrdinalIgnoreCase));
        bool ridType = identity.PackageTypes.Any(t =>
            string.Equals(t, FuncCliWorkloadRidPackageType, StringComparison.OrdinalIgnoreCase));
        if (standardType && ridType)
        {
            throw new InvalidWorkloadException(
                $"Package '{identity.PackageId}' declares both '{FuncCliWorkloadPackageType}' and '{FuncCliWorkloadRidPackageType}' package types.");
        }

        if (metadata.Kind == WorkloadKind.RidPointer)
        {
            RequirePackageType(identity, standardType, FuncCliWorkloadPackageType);
            if (identity.RidTags.Count > 0)
            {
                throw new InvalidWorkloadException(
                    $"RID pointer package '{identity.PackageId}' cannot declare a rid: tag.");
            }

            if (files.Any(IsPayloadFile))
            {
                throw new InvalidWorkloadException(
                    $"RID pointer package '{identity.PackageId}' cannot contain a tools/ payload.");
            }

            ValidatePointerMap(identity, metadata);
            return PackageRole.Pointer;
        }

        if (ridType)
        {
            if (metadata.Kind is not (WorkloadKind.Workload or WorkloadKind.Content))
            {
                throw new InvalidWorkloadException(
                    $"RID implementation package '{identity.PackageId}' must declare kind 'workload' or 'content'.");
            }

            if (string.IsNullOrWhiteSpace(metadata.RuntimeIdentifier))
            {
                throw new InvalidWorkloadException(
                    $"RID implementation package '{identity.PackageId}' is missing runtimeIdentifier.");
            }

            ValidateImplementationRid(identity, metadata.RuntimeIdentifier);
            return PackageRole.RidImplementation;
        }

        RequirePackageType(identity, standardType, FuncCliWorkloadPackageType);
        if (metadata.RuntimeIdentifier is not null || identity.RidTags.Count > 0)
        {
            throw new InvalidWorkloadException(
                $"Package '{identity.PackageId}' uses RID implementation metadata but does not declare package type '{FuncCliWorkloadRidPackageType}'.");
        }

        return PackageRole.Ordinary;
    }

    private void ValidateImplementationRid(PackageIdentity identity, string manifestRuntimeIdentifier)
    {
        string currentRuntimeIdentifier = CurrentRuntimeIdentifier;
        if (identity.RidTags.Count != 1)
        {
            throw new InvalidWorkloadException(
                $"RID implementation package '{identity.PackageId}' must declare exactly one rid:<rid> tag.");
        }

        string tagRuntimeIdentifier = identity.RidTags[0];
        string expectedSuffix = "." + manifestRuntimeIdentifier;
        if (!string.Equals(tagRuntimeIdentifier, manifestRuntimeIdentifier, StringComparison.Ordinal)
            || !identity.PackageId.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(manifestRuntimeIdentifier, currentRuntimeIdentifier, StringComparison.Ordinal))
        {
            throw new InvalidWorkloadException(
                $"RID metadata mismatch for package '{identity.PackageId}': manifest runtimeIdentifier='{manifestRuntimeIdentifier}', " +
                $"nuspec rid tag='{tagRuntimeIdentifier}', package-id suffix='{expectedSuffix}', current RID='{currentRuntimeIdentifier}'.");
        }
    }

    private static void ValidatePointerMap(PackageIdentity identity, WorkloadMetadata metadata)
    {
        foreach ((string runtimeIdentifier, string implementationId) in metadata.Packages!)
        {
            string expected = $"{identity.PackageId}.{runtimeIdentifier}";
            if (!string.Equals(implementationId, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidWorkloadException(
                    $"RID pointer package '{identity.PackageId}' maps runtime identifier '{runtimeIdentifier}' to '{implementationId}', " +
                    $"but the required implementation id is '{expected}'.");
            }
        }
    }

    private PointerSelection SelectPointerImplementation(InspectedPackage pointer)
    {
        string currentRuntimeIdentifier = CurrentRuntimeIdentifier;
        if (!pointer.Metadata.Packages!.TryGetValue(currentRuntimeIdentifier, out string? implementationId))
        {
            string supported = string.Join(
                ", ",
                pointer.Metadata.Packages.Keys.OrderBy(r => r, StringComparer.Ordinal));
            throw new WorkloadPackageNotFoundException(
                $"Workload '{pointer.Identity.PackageId}' {pointer.Identity.Version} does not support runtime identifier " +
                $"'{currentRuntimeIdentifier}'. Supported runtime identifiers: {supported}.");
        }

        return new PointerSelection(currentRuntimeIdentifier, implementationId.ToLowerInvariant());
    }

    private static void ValidatePointerImplementation(
        InspectedPackage pointer,
        PointerSelection selection,
        InspectedPackage implementation)
    {
        if (implementation.Role != PackageRole.RidImplementation)
        {
            throw new InvalidWorkloadException(
                $"Pointer '{pointer.Identity.PackageId}' selected '{implementation.Identity.PackageId}', " +
                $"but it is not a '{FuncCliWorkloadRidPackageType}' implementation package.");
        }

        if (!string.Equals(implementation.Identity.PackageId, selection.PackageId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(implementation.Identity.Version, pointer.Identity.Version, StringComparison.Ordinal)
            || !string.Equals(implementation.Metadata.RuntimeIdentifier, selection.RuntimeIdentifier, StringComparison.Ordinal))
        {
            throw new InvalidWorkloadException(
                $"Pointer implementation mismatch: pointer='{pointer.Identity.PackageId}' {pointer.Identity.Version}, " +
                $"map RID='{selection.RuntimeIdentifier}', mapped package='{selection.PackageId}', returned package=" +
                $"'{implementation.Identity.PackageId}' {implementation.Identity.Version}, manifest runtimeIdentifier=" +
                $"'{implementation.Metadata.RuntimeIdentifier}'.");
        }
    }

    private async Task<ResolvedPackage> ResolvePointerImplementationAsync(
        InspectedPackage pointer,
        PointerSelection selection,
        string source,
        CancellationToken cancellationToken)
    {
        var version = NuGetVersion.Parse(pointer.Identity.Version);
        ResolvedPackage? resolved = await _catalog.ResolveVersionAsync(
            selection.PackageId,
            version,
            source,
            cancellationToken);
        if (resolved is null)
        {
            throw CreatePartialPublishException(pointer, selection, source);
        }

        return resolved;
    }

    private static WorkloadPackageNotFoundException CreatePartialPublishException(
        InspectedPackage pointer,
        PointerSelection selection,
        string source)
        => new(
            $"RID pointer '{pointer.Identity.PackageId}' {pointer.Identity.Version} selected runtime identifier " +
            $"'{selection.RuntimeIdentifier}', but implementation '{selection.PackageId}' {pointer.Identity.Version} " +
            $"was not found on source '{source}'. This may be a partial publish or feed indexing delay.");

    private string FindLocalImplementation(
        string pointerPath,
        string packageId,
        string version,
        string runtimeIdentifier)
    {
        string directory = Path.GetDirectoryName(pointerPath)!;
        string expectedFileName = $"{packageId}.{version}.nupkg";

        // Probe the conventional file name first so an unrelated or corrupt sibling neither slows the
        // lookup down nor aborts the install.
        IOrderedEnumerable<string> candidates = Directory
            .EnumerateFiles(directory, "*.nupkg", SearchOption.TopDirectoryOnly)
            .OrderByDescending(c => string.Equals(Path.GetFileName(c), expectedFileName, StringComparison.OrdinalIgnoreCase));
        foreach (string candidate in candidates)
        {
            if (string.Equals(candidate, pointerPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (MatchesPackageIdentity(candidate, packageId, version))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"Local RID pointer '{Path.GetFileName(pointerPath)}' requires implementation '{packageId}' {version} for RID " +
            $"'{runtimeIdentifier}' in directory '{directory}'. No configured feed was searched.");
    }

    private static bool MatchesPackageIdentity(string packagePath, string packageId, string version)
    {
        try
        {
            using PackageArchiveReader reader = OpenPackage(packagePath);
            NuspecReader nuspec = reader.NuspecReader;
            return string.Equals(nuspec.GetId(), packageId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(nuspec.GetVersion().ToNormalizedString(), version, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is InvalidWorkloadException or IOException or UnauthorizedAccessException)
        {
            // An unreadable sibling cannot be the implementation we need; keep probing the directory.
            return false;
        }
    }

    private async Task<string> ResolveAliasOrIdAsync(
        string aliasOrId,
        string? source,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        const int aliasSearchTake = 50;
        CatalogSearchQuery query = new()
        {
            Filter = aliasOrId,
            IncludePrerelease = includePrerelease,
            Take = aliasSearchTake,
            Source = source,
        };

        IReadOnlyList<CatalogSearchResult> hits = await _catalog.SearchAsync(query, cancellationToken);
        IReadOnlyList<CatalogSearchResult> aliasMatches = FilterByAlias(hits, aliasOrId);
        if (aliasMatches.Count == 0 && hits.Count == 0)
        {
            IReadOnlyList<CatalogSearchResult> all = await _catalog.SearchAsync(
                query with { Filter = null },
                cancellationToken);
            aliasMatches = FilterByAlias(all, aliasOrId);
        }

        IReadOnlyList<string> pointerIds = DistinctPackageIds(aliasMatches.Where(m =>
            string.Equals(m.Kind, "rid-pointer", StringComparison.OrdinalIgnoreCase)));
        if (pointerIds.Count == 1)
        {
            return pointerIds[0];
        }

        if (pointerIds.Count > 1)
        {
            throw new AmbiguousPackageMatchException(aliasOrId, pointerIds);
        }

        IReadOnlyList<string> matchedIds = DistinctPackageIds(aliasMatches);
        if (matchedIds.Count > 1)
        {
            throw new AmbiguousPackageMatchException(aliasOrId, matchedIds);
        }

        return matchedIds.Count == 1 ? matchedIds[0] : aliasOrId;
    }

    private async Task<ResolvedPackage> ResolveCatalogPackageAsync(
        string packageId,
        NuGetVersion? version,
        string? source,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        ResolvedPackage? resolved = version is null
            ? await _catalog.ResolveLatestVersionAsync(
                packageId,
                includePrerelease,
                currentVersion: null,
                allowMajor: true,
                source,
                cancellationToken)
            : await _catalog.ResolveVersionAsync(packageId, version, source, cancellationToken);
        if (resolved is null)
        {
            string detail = version is null
                ? "No matching version was found on any configured source."
                : $"Version '{version.ToNormalizedString()}' was not found on any configured source.";
            string hint = includePrerelease ? string.Empty : " Pass --prerelease if it is a prerelease.";
            throw new WorkloadPackageNotFoundException(
                $"Could not resolve workload '{packageId}'. {detail}{hint}");
        }

        return resolved;
    }

    private async Task<string> DownloadToTempAsync(
        ResolvedPackage resolved,
        IProgress<WorkloadInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new WorkloadInstallProgress(
            WorkloadInstallPhase.Downloading,
            $"Downloading '{resolved.PackageId}' {resolved.Version.ToNormalizedString()}"));
        string path = Path.Combine(Path.GetTempPath(), $"func-workload-{Guid.NewGuid():N}.nupkg");
        try
        {
            await using Stream packageStream = await _catalog.DownloadAsync(resolved, cancellationToken);
            await using FileStream tempStream = File.Create(path);
            await packageStream.CopyToAsync(tempStream, cancellationToken);
            return path;
        }
        catch
        {
            TryDeleteFile(path);
            throw;
        }
    }

    private static WorkloadEntry CreateEntry(
        InspectedPackage package,
        string source,
        WorkloadOwnershipKind ownership,
        LogicalPackage? logicalPackage)
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

    private static LogicalPackage CreateLogicalPackage(InspectedPackage pointer, string source)
        => new()
        {
            PackageId = pointer.Identity.PackageId,
            PackageVersion = pointer.Identity.Version,
            Aliases = pointer.Identity.Aliases,
            DisplayName = GetDisplayName(pointer.Metadata, pointer.Identity.Title, pointer.Identity.PackageId),
            Description = GetDescription(pointer.Metadata, pointer.Identity.Description),
            Source = source,
        };

    private static WorkloadEntry CopyForOwnership(
        WorkloadEntry entry,
        WorkloadOwnershipKind ownership,
        LogicalPackage? logicalPackage)
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

    private static WorkloadEntry MergeForReinstall(
        WorkloadEntry existing,
        WorkloadEntry incoming,
        WorkloadOwnershipKind ownership)
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

    private static WorkloadEntry ResolveUpdateTarget(
        string packageId,
        NuGetVersion? targetInstalledVersion,
        IReadOnlyList<WorkloadEntry> matches)
    {
        if (targetInstalledVersion is null)
        {
            return matches.OrderByDescending(e => NuGetVersion.Parse(e.PackageVersion)).First();
        }

        string requested = targetInstalledVersion.ToNormalizedString();
        WorkloadEntry? exact = matches.FirstOrDefault(e =>
            string.Equals(e.PackageVersion, requested, StringComparison.Ordinal));
        if (exact is null)
        {
            string available = string.Join(", ", matches.Select(m => m.PackageVersion));
            throw new InvalidOperationException(
                $"Workload '{packageId}' version '{requested}' is not installed. Installed versions: {available}.");
        }

        return exact;
    }

    private static WorkloadEntry ResolveLogicalUpdateTarget(
        string packageId,
        NuGetVersion? targetInstalledVersion,
        IReadOnlyList<WorkloadEntry> matches)
    {
        if (targetInstalledVersion is null)
        {
            return matches
                .OrderByDescending(e => NuGetVersion.Parse(e.LogicalPackage!.PackageVersion))
                .First();
        }

        string requested = targetInstalledVersion.ToNormalizedString();
        WorkloadEntry? exact = matches.FirstOrDefault(e =>
            string.Equals(e.LogicalPackage!.PackageVersion, requested, StringComparison.Ordinal));
        if (exact is null)
        {
            string available = string.Join(", ", matches.Select(e => e.LogicalPackage!.PackageVersion));
            throw new InvalidOperationException(
                $"Logical workload '{packageId}' version '{requested}' is not installed. Installed versions: {available}.");
        }

        return exact;
    }

    private static void ValidateResolvedIdentity(ResolvedPackage resolved, PackageIdentity identity)
    {
        if (!string.Equals(identity.PackageId, resolved.PackageId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(identity.Version, resolved.Version.ToNormalizedString(), StringComparison.Ordinal))
        {
            throw new InvalidWorkloadException(
                $"Resolved package '{resolved.PackageId}' {resolved.Version.ToNormalizedString()} but the source returned " +
                $"'{identity.PackageId}' {identity.Version}.");
        }
    }

    private static void EnsureMetadataMatchesInspection(
        WorkloadMetadata expected,
        WorkloadMetadata actual,
        string packageId)
    {
        if (expected.Kind != actual.Kind
            || !string.Equals(expected.RuntimeIdentifier, actual.RuntimeIdentifier, StringComparison.Ordinal))
        {
            throw new InvalidWorkloadException(
                $"Package '{packageId}' workload.json changed while the package was being installed.");
        }
    }

    private static void RequirePackageType(PackageIdentity identity, bool hasType, string expectedType)
    {
        if (!hasType)
        {
            throw new InvalidWorkloadException(
                $"Package '{identity.PackageId}' is missing required package type '{expectedType}' in its .nuspec.");
        }
    }

    private string CurrentRuntimeIdentifier
    {
        get
        {
            string runtimeIdentifier = _runtimeIdentifierProvider.Current.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(runtimeIdentifier))
            {
                throw new InvalidOperationException("Unable to determine the current runtime identifier.");
            }

            return runtimeIdentifier;
        }
    }

    private bool IncludePrerelease(bool? includePrerelease)
        => includePrerelease ?? _catalogOptions.IncludePrerelease;

    private static bool IsLocalPackageSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        if (Path.IsPathFullyQualified(source))
        {
            return true;
        }

        return Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && uri.IsFile;
    }

    private static IReadOnlyList<CatalogSearchResult> FilterByAlias(
        IReadOnlyList<CatalogSearchResult> hits,
        string alias)
        => [.. hits.Where(r => r.Aliases.Any(a =>
            string.Equals(a, alias, StringComparison.OrdinalIgnoreCase)))];

    private static IReadOnlyList<string> DistinctPackageIds(IEnumerable<CatalogSearchResult> hits)
        => [.. hits.Select(r => r.PackageId).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static PackageArchiveReader OpenPackage(string nupkgPath)
    {
        try
        {
            return new PackageArchiveReader(File.OpenRead(nupkgPath));
        }
        catch (Exception ex) when (ex is InvalidDataException or PackagingException)
        {
            throw new InvalidWorkloadException(
                $"Failed to read .nupkg at '{nupkgPath}': {ex.Message}",
                ex);
        }
    }

    private static Task ExtractPackageAsync(
        PackageArchiveReader reader,
        string destination,
        CancellationToken cancellationToken)
        => ExtractPackageFilesAsync(reader, destination, IsInstallablePackageFile, cancellationToken);

    /// <summary>
    /// Extracts only the workload manifest. Inspection happens before staging, so pulling the payload
    /// out here would extract every package twice.
    /// </summary>
    private static Task ExtractMetadataOnlyAsync(
        PackageArchiveReader reader,
        string destination,
        CancellationToken cancellationToken)
        => ExtractPackageFilesAsync(reader, destination, IsMetadataFile, cancellationToken);

    private static async Task ExtractPackageFilesAsync(
        PackageArchiveReader reader,
        string destination,
        Func<string, bool> include,
        CancellationToken cancellationToken)
    {
        foreach (string packageFile in await reader.GetFilesAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!include(packageFile))
            {
                continue;
            }

            string targetPath = Path.Combine(
                destination,
                packageFile.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            using Stream entryStream = await reader.GetStreamAsync(packageFile, cancellationToken);
            using FileStream output = File.Create(targetPath);
            await entryStream.CopyToAsync(output, cancellationToken);
        }
    }

    private static bool IsMetadataFile(string packageFile)
        => string.Equals(
            packageFile,
            WorkloadMetadataReader.MetadataFileName,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsInstallablePackageFile(string packageFile)
        => IsMetadataFile(packageFile) || IsPayloadFile(packageFile);

    private static bool IsPayloadFile(string packageFile)
        => packageFile.StartsWith("tools/", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ParseAliases(string? tags)
        => ParseTagValues(tags, AliasTagPrefix);

    private static IReadOnlyList<string> ParseTagValues(string? tags, string prefix)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return [];
        }

        List<string> values = [];
        foreach (string tag in tags.Split(
            [' ', ','],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string value = tag[prefix.Length..].Trim();
                if (value.Length > 0)
                {
                    values.Add(value.ToLowerInvariant());
                }
            }
        }

        return values;
    }

    private static void EnsureHostExecutableBit(string installPath, string packageId, string? runtimeIdentifier)
    {
        if (OperatingSystem.IsWindows()
            || !packageId.StartsWith(HostWorkloadPackage.PackageIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string hostBinary = Path.Combine(
            WorkloadPackageLayout.GetContentRoot(installPath, runtimeIdentifier),
            HostProcessStartInfoFactory.ExecutableBaseName);
        if (!File.Exists(hostBinary))
        {
            return;
        }

        UnixFileMode mode = File.GetUnixFileMode(hostBinary);
        File.SetUnixFileMode(
            hostBinary,
            mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    private static string GetDisplayName(
        WorkloadMetadata metadata,
        string? nuspecTitle,
        string packageId)
        => !string.IsNullOrWhiteSpace(metadata.DisplayName)
            ? metadata.DisplayName
            : string.IsNullOrWhiteSpace(nuspecTitle) ? packageId : nuspecTitle;

    private static string GetDescription(
        WorkloadMetadata metadata,
        string? nuspecDescription)
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

    /// <summary>
    /// Removes an install directory before a staged payload replaces it. Unlike best-effort cleanup this
    /// must succeed: a partial delete would let the move merge the new payload into stale files.
    /// </summary>
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

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup; temporary files are reaped by the OS.
        }
    }

    private enum PackageRole
    {
        Ordinary,
        Pointer,
        RidImplementation,
    }

    private sealed record PackageIdentity(
        string PackageId,
        string Version,
        IReadOnlyList<string> PackageTypes,
        IReadOnlyList<string> Aliases,
        IReadOnlyList<string> RidTags,
        string? Title,
        string? Description);

    private sealed record InspectedPackage(
        string Path,
        PackageIdentity Identity,
        WorkloadMetadata Metadata,
        PackageRole Role);

    private sealed record PointerSelection(string RuntimeIdentifier, string PackageId);
}
