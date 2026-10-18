// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Commands.Start.Host;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Install;

internal sealed class WorkloadDeploymentService(IWorkloadPaths paths, IWorkloadStore store, IWorkloadMetadataReader metadataReader,
    IWorkloadOperationCoordinator coordinator, IFileSystem fileSystem, IBundledTemplateInstaller bundledTemplates)
    : IWorkloadDeploymentService
{
    private readonly IWorkloadPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IWorkloadStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IWorkloadMetadataReader _metadataReader =
        metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
    private readonly IWorkloadOperationCoordinator _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IBundledTemplateInstaller _bundledTemplates = bundledTemplates ?? throw new ArgumentNullException(nameof(bundledTemplates));

    public bool HasBundledTemplates(WorkloadEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        string directory = _paths.GetInstallDirectory(entry.PackageId, entry.PackageVersion);
        return entry.BundledTemplatesDeclared || _fileSystem.FileExists(Path.Combine(directory, BundledTemplateInstaller.ManifestName))
            || _fileSystem.FileExists(Path.Combine(directory, BundledTemplateInstaller.OriginName));
    }

    public async Task<WorkloadEntry> GetUpdateTargetAsync(string packageId, NuGetVersion? targetInstalledVersion,
        WorkloadOwnershipKind ownership, CancellationToken cancellationToken = default)
    {
        using IWorkloadOperation operation = await _coordinator.AcquireAsync(cancellationToken);
        return await GetUpdateTargetAsync(operation, packageId, targetInstalledVersion, ownership, cancellationToken);
    }

    public async Task<WorkloadEntry> GetUpdateTargetAsync(IWorkloadOperation operation, string packageId, NuGetVersion? targetInstalledVersion,
        WorkloadOwnershipKind ownership, CancellationToken cancellationToken)
    {
        ValidateOperation(operation);
        return await GetAdvisoryUpdateTargetAsync(packageId, targetInstalledVersion, ownership, cancellationToken);
    }

    public async Task<WorkloadEntry> GetAdvisoryUpdateTargetAsync(string packageId, NuGetVersion? targetInstalledVersion,
        WorkloadOwnershipKind ownership, CancellationToken cancellationToken)
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
            !entry.IsImplicitlyInstalled
            && string.Equals(entry.PackageId, packageId, StringComparison.OrdinalIgnoreCase))];
        if (explicitMatches.Count == 0)
        {
            throw new InvalidOperationException($"Explicit ownership for workload '{packageId}' is not installed.");
        }

        return ResolveUpdateTarget(packageId, targetInstalledVersion, explicitMatches);
    }

    public async Task<WorkloadInstallResult?> TryReuseExplicitAsync(string packageId, string version, CancellationToken cancellationToken = default)
    {
        using IWorkloadOperation operation = await _coordinator.AcquireAsync(cancellationToken);
        return await TryReuseExplicitAsync(operation, packageId, version, cancellationToken);
    }

    public async Task<WorkloadInstallResult?> TryReuseExplicitAsync(IWorkloadOperation operation, string packageId, string version,
        CancellationToken cancellationToken)
    {
        ValidateOperation(operation);
        return await GetAdvisoryExplicitReuseAsync(packageId, version, cancellationToken);
    }

    public async Task<WorkloadInstallResult?> GetAdvisoryExplicitReuseAsync(string packageId, string version, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);
        WorkloadEntry? existing = installed.FirstOrDefault(entry =>
            string.Equals(entry.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.PackageVersion, version, StringComparison.Ordinal));
        if (existing is null
            || existing.IsImplicitlyInstalled
            || !Directory.Exists(_paths.GetInstallDirectory(existing.PackageId, existing.PackageVersion)))
        {
            return null;
        }

        return new WorkloadInstallResult(existing, true);
    }

    public async Task<WorkloadEntry?> GetReusableImplementationAsync(string packageId, string version, string runtimeIdentifier,
        CancellationToken cancellationToken)
        => FindReusableImplementation(await _store.GetWorkloadsAsync(cancellationToken), packageId, version, runtimeIdentifier);

    public async Task<PreparedWorkloadReuse> PrepareReuseAsync(WorkloadEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        string directory = _paths.GetInstallDirectory(entry.PackageId, entry.PackageVersion);
        WorkloadMetadata metadata;
        try
        {
            metadata = _metadataReader.Read(directory);
        }
        catch (Exception exception) when (exception is InvalidWorkloadException or DirectoryNotFoundException)
        {
            throw new InvalidWorkloadException($"The installed workload '{entry.PackageId}' {entry.PackageVersion} payload is damaged. " +
                "Preserve the installation and explicitly repair it from its original source.", exception);
        }
        if (metadata.Kind != entry.Kind || metadata.RuntimeIdentifier != entry.RuntimeIdentifier
            || metadata.EntryPoint?.AssemblyPath != entry.EntryPoint?.AssemblyPath || metadata.EntryPoint?.Type != entry.EntryPoint?.Type
            || !_fileSystem.DirectoryExists(Path.Combine(directory, WorkloadMetadataReader.ContentDirectoryName))
            || (metadata.EntryPoint is { } entryPoint && !_fileSystem.FileExists(Path.Combine(directory, entryPoint.AssemblyPath))))
        {
            throw new InvalidWorkloadException($"The installed workload '{entry.PackageId}' {entry.PackageVersion} payload is damaged or conflicts with its registry entry. " +
                "Preserve the installation and explicitly repair it from its original source.");
        }
        Dictionary<string, string> hashes = new(StringComparer.Ordinal);
        foreach (string file in _fileSystem.GetFiles(directory))
        {
            hashes.Add(Path.GetRelativePath(directory, file), await _fileSystem.ComputeSha256Async(file, cancellationToken));
        }

        PreparedBundledTemplates? templates = null;
        if (HasBundledTemplates(entry))
        {
            string originJson = await _fileSystem.ReadAllTextIfExistsAsync(Path.Combine(directory, BundledTemplateInstaller.OriginName), cancellationToken)
                ?? throw new InvalidWorkloadException("Bundled workload provenance is missing. Preserve the installation and request explicit repair.");
            BundledTemplateOrigin origin;
            try
            {
                origin = JsonSerializer.Deserialize(originJson, BundledTemplateJsonContext.Default.BundledTemplateOrigin)
                    ?? throw new InvalidWorkloadException("Bundled workload provenance is invalid.");
            }
            catch (JsonException exception)
            {
                throw new InvalidWorkloadException("Bundled workload provenance is malformed. Preserve the installation and request explicit repair.", exception);
            }
            // Logical provenance records acquisition, including after that selected owner moves away.
            if (origin.PackageId != entry.PackageId || origin.Version != entry.PackageVersion || origin.Source != entry.Source
                || (entry.LogicalPackage is not null
                    && (origin.LogicalPackageId != entry.LogicalPackage.PackageId || origin.LogicalVersion != entry.LogicalPackage.PackageVersion
                        || origin.LogicalSource != entry.LogicalPackage.Source)))
            {
                throw new WorkloadPlanChangedException();
            }

            templates = await _bundledTemplates.PrepareAsync(directory, origin, cancellationToken)
                ?? throw new InvalidWorkloadException("The declared bundled-template manifest changed during preparation.");
        }

        return new(entry, hashes) { BundledTemplates = templates };
    }

    public async Task ValidateReuseAsync(IWorkloadOperation operation, PreparedWorkloadReuse prepared, CancellationToken cancellationToken, bool checkOnly = false)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        await ValidateCurrentEntryAsync(operation, prepared.Entry, cancellationToken);
        await ValidateHashesAsync(prepared.Hashes, _paths.GetInstallDirectory(prepared.Entry.PackageId, prepared.Entry.PackageVersion), cancellationToken);
        if (checkOnly && prepared.BundledTemplates is not null)
        {
            await _bundledTemplates.ValidateInstalledAsync(prepared.BundledTemplates, cancellationToken);
        }
    }

    public async Task<WorkloadInstallResult> CommitPreparedReuseAsync(IWorkloadOperation operation, PreparedWorkloadReuse prepared,
        Func<CancellationToken, Task<WorkloadInstallResult>> admitWorkload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(admitWorkload);
        ValidateOperation(operation);
        if (prepared.BundledTemplates is null)
        {
            if (HasBundledTemplates(prepared.Entry))
            {
                throw new InvalidWorkloadException("Required bundled-template preparation evidence is missing. No registration was performed.");
            }

            await ValidateReuseAsync(operation, prepared, cancellationToken);
            return await admitWorkload(cancellationToken);
        }

        return await _bundledTemplates.CommitAsync(prepared.BundledTemplates, async token =>
        {
            await ValidateReuseAsync(operation, prepared, token);
            WorkloadInstallResult admitted = await admitWorkload(token);
            await ValidateCurrentEntryAsync(operation, admitted.Entry, CancellationToken.None);
            await ValidateHashesAsync(prepared.Hashes, _paths.GetInstallDirectory(admitted.Entry.PackageId, admitted.Entry.PackageVersion),
                CancellationToken.None);
            return admitted;
        }, cancellationToken);
    }

    public async Task<WorkloadInstallResult?> TryReuseImplementationAsync(string packageId, string version, string runtimeIdentifier,
        LogicalPackage logicalPackage, CancellationToken cancellationToken = default)
    {
        using IWorkloadOperation operation = await _coordinator.AcquireAsync(cancellationToken);
        WorkloadInstallResult? result = await TryReuseImplementationAsync(operation, packageId, version, runtimeIdentifier, logicalPackage, cancellationToken);
        operation.CompleteMutation();
        return result;
    }

    public Task<WorkloadInstallResult?> TryReuseImplementationAsync(IWorkloadOperation operation, string packageId, string version,
        string runtimeIdentifier, LogicalPackage logicalPackage, CancellationToken cancellationToken)
        => TryReuseImplementationCoreAsync(operation, packageId, version, runtimeIdentifier, logicalPackage, currentLogicalOwner: null, cancellationToken);

    public async Task<WorkloadInstallResult?> TryReuseImplementationForUpdateAsync(WorkloadEntry currentEntry, string packageId, string version,
        string runtimeIdentifier, LogicalPackage logicalPackage, CancellationToken cancellationToken = default)
    {
        using IWorkloadOperation operation = await _coordinator.AcquireAsync(cancellationToken);
        WorkloadInstallResult? result = await TryReuseImplementationForUpdateAsync(operation, currentEntry, packageId, version,
            runtimeIdentifier, logicalPackage, cancellationToken);
        operation.CompleteMutation();
        return result;
    }

    public async Task<WorkloadInstallResult?> TryReuseImplementationForUpdateAsync(IWorkloadOperation operation, WorkloadEntry currentEntry,
        string packageId, string version, string runtimeIdentifier, LogicalPackage logicalPackage, CancellationToken cancellationToken)
    {
        await ValidateCurrentEntryAsync(operation, currentEntry, cancellationToken);
        return await TryReuseImplementationCoreAsync(operation, packageId, version, runtimeIdentifier, logicalPackage, currentEntry, cancellationToken);
    }

    public async Task<PreparedWorkloadDeployment> PrepareAsync(InspectedWorkloadPackage package, string source,
        WorkloadOwnershipKind ownership, LogicalPackage? logicalPackage, bool force,
        IProgress<WorkloadInstallProgress>? progress, CancellationToken cancellationToken, WorkloadEntry? updateFrom = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        WorkloadOwnershipRules.ValidateDestination(null, logicalPackage, ownership);
        if (await _fileSystem.ComputeSha256Async(package.Path, cancellationToken) != package.Sha256)
        {
            throw new WorkloadPlanChangedException();
        }

        if (package.Role == WorkloadPackageRole.Pointer)
        {
            throw new InvalidWorkloadException($"Pointer package '{package.Identity.PackageId}' cannot be installed as a physical workload.");
        }

        string expectedState = WorkloadStateSnapshot.CaptureAffected(await _store.GetWorkloadsAsync(cancellationToken),
            package.Identity.PackageId, logicalPackage?.PackageId);
        TempDirectory staging = _fileSystem.CreateTempDirectory();
        PreparedBundledTemplates? bundled = null;
        PreparedWorkloadReuse? previous = null;
        PreparedWorkloadReuse? destination = null;
        List<PreparedWorkloadReuse> retained = [];
        try
        {
            if (updateFrom is null)
            {
                IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);
                foreach (WorkloadEntry sibling in installed.Where(entry =>
                    string.Equals(entry.PackageId, package.Identity.PackageId, StringComparison.OrdinalIgnoreCase)
                    && entry.PackageVersion != package.Identity.Version && HasBundledTemplates(entry)))
                {
                    PreparedWorkloadReuse reuse = await PrepareReuseAsync(sibling, cancellationToken);
                    retained.Add(reuse);
                    await _bundledTemplates.ValidateInstalledAsync(reuse.BundledTemplates!, cancellationToken);
                }
            }
            progress?.Report(new(WorkloadInstallPhase.Extracting, $"Extracting workload '{package.Identity.PackageId}' {package.Identity.Version}"));
            using (PackageArchiveReader reader = OpenPackage(package.Path))
            {
                await ExtractPackageAsync(reader, staging.Path, cancellationToken);
            }

            if (await _fileSystem.ComputeSha256Async(package.Path, cancellationToken) != package.Sha256)
            {
                throw new WorkloadPlanChangedException();
            }

            WorkloadMetadata metadata = _metadataReader.Read(staging.Path);
            EnsureMetadataMatchesInspection(package.Metadata, metadata, package.Identity.PackageId);
            EnsureHostExecutableBit(staging.Path, package.Identity.PackageId, metadata.RuntimeIdentifier);
            if (_fileSystem.FileExists(Path.Combine(staging.Path, BundledTemplateInstaller.ManifestName)))
            {
                if (metadata.Kind != WorkloadKind.Workload)
                {
                    throw new InvalidWorkloadException("Bundled templates must be declared by a physical stack workload.");
                }

                if (updateFrom is not null)
                {
                    previous = await PrepareReuseAsync(updateFrom, cancellationToken);
                }
                BundledTemplateOrigin origin = new(package.Identity.PackageId, package.Identity.Version, package.Sha256, source,
                    logicalPackage?.PackageId, logicalPackage?.PackageVersion, logicalPackage?.Source);
                if (previous is not null)
                {
                    WorkloadEntry? existing = (await _store.GetWorkloadsAsync(cancellationToken)).SingleOrDefault(entry =>
                        string.Equals(entry.PackageId, package.Identity.PackageId, StringComparison.OrdinalIgnoreCase)
                        && entry.PackageVersion == package.Identity.Version);
                    if (existing is not null && HasBundledTemplates(existing))
                    {
                        WorkloadOwnershipRules.ValidateDestination(existing, logicalPackage, ownership);
                        destination = await PrepareReuseAsync(existing, cancellationToken);
                        BundledTemplateOrigin installedOrigin = destination.BundledTemplates!.Origin;
                        LogicalPackage? retainedLogical = existing.LogicalPackage ?? logicalPackage;
                        if (installedOrigin.PackageId != origin.PackageId || installedOrigin.Version != origin.Version
                            || installedOrigin.Sha256 != origin.Sha256 || installedOrigin.Source != origin.Source
                            || (retainedLogical is not null
                                && (installedOrigin.LogicalPackageId != retainedLogical.PackageId
                                    || installedOrigin.LogicalVersion != retainedLogical.PackageVersion
                                    || installedOrigin.LogicalSource != retainedLogical.Source))
                            || (ownership == WorkloadOwnershipKind.Logical
                                && (installedOrigin.LogicalPackageId != origin.LogicalPackageId
                                    || installedOrigin.LogicalVersion != origin.LogicalVersion
                                    || installedOrigin.LogicalSource != origin.LogicalSource)))
                        {
                            throw new InvalidWorkloadException("The installed update destination has a different validated origin. " +
                                "No source, ownership, or payload replacement was performed.");
                        }
                        origin = installedOrigin;
                    }
                }
                bundled = await _bundledTemplates.PrepareAsync(staging.Path, origin, cancellationToken, previous?.BundledTemplates,
                    previous?.Entry.InstallRefCount > 1)
                    ?? throw new InvalidWorkloadException("The declared bundled-template manifest changed during preparation.");
                await _fileSystem.WriteAllTextAsync(Path.Combine(staging.Path, BundledTemplateInstaller.OriginName),
                    JsonSerializer.Serialize(origin, BundledTemplateJsonContext.Default.BundledTemplateOrigin), cancellationToken);
            }

            Dictionary<string, string> hashes = new(StringComparer.Ordinal);
            foreach (string path in _fileSystem.GetFiles(staging.Path))
            {
                hashes.Add(Path.GetRelativePath(staging.Path, path), await _fileSystem.ComputeSha256Async(path, cancellationToken));
            }

            return new(staging, package, source, ownership, logicalPackage, force, hashes, expectedState)
            {
                BundledTemplates = bundled,
                UpdateFrom = previous,
                Destination = destination,
                Retained = retained.AsReadOnly(),
            };
        }
        catch
        {
            bundled?.Dispose();
            previous?.Dispose();
            destination?.Dispose();
            foreach (PreparedWorkloadReuse reuse in retained)
            {
                reuse.Dispose();
            }
            staging.Dispose();
            throw;
        }
    }

    public async Task<WorkloadInstallResult> InstallAsync(InspectedWorkloadPackage package, string source, WorkloadOwnershipKind ownership,
        LogicalPackage? logicalPackage, bool force, IProgress<WorkloadInstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        using PreparedWorkloadDeployment prepared = await PrepareAsync(package, source, ownership, logicalPackage, force, progress, cancellationToken);
        using IWorkloadOperation operation = await _coordinator.AcquireAsync(cancellationToken);
        return await InstallPreparedAsync(operation, prepared, progress, cancellationToken);
    }

    public async Task<WorkloadInstallResult> InstallPreparedAsync(IWorkloadOperation operation, PreparedWorkloadDeployment prepared,
        IProgress<WorkloadInstallProgress>? progress, CancellationToken cancellationToken)
    {
        ValidateOperation(operation);
        ArgumentNullException.ThrowIfNull(prepared);
        IReadOnlyList<PreparedBundledTemplates> retained = [.. prepared.Retained.Select(owner => owner.BundledTemplates!)];
        if (prepared.BundledTemplates is not null)
        {
            return await _bundledTemplates.CommitAsync(prepared.BundledTemplates,
                token => InstallPreparedCoreAsync(operation, prepared, progress, token), cancellationToken, retained: retained);
        }
        return retained.Count == 0
            ? await InstallPreparedCoreAsync(operation, prepared, progress, cancellationToken)
            : await _bundledTemplates.WithRetainedOwnersAsync(retained,
                token => InstallPreparedCoreAsync(operation, prepared, progress, token), cancellationToken);
    }

    private async Task<WorkloadInstallResult> InstallPreparedCoreAsync(IWorkloadOperation operation, PreparedWorkloadDeployment prepared,
        IProgress<WorkloadInstallProgress>? progress, CancellationToken cancellationToken)
    {
        ValidateOperation(operation);
        ArgumentNullException.ThrowIfNull(prepared);
        await ValidatePreparedAsync(prepared, cancellationToken);
        foreach (PreparedWorkloadReuse retained in prepared.Retained)
        {
            await ValidateReuseAsync(operation, retained, cancellationToken);
        }
        InspectedWorkloadPackage package = prepared.Package;
        WorkloadOwnershipKind ownership = prepared.Ownership;
        LogicalPackage? logicalPackage = prepared.LogicalPackage;
        string packageId = package.Identity.PackageId;
        string version = package.Identity.Version;
        string installPath = _paths.GetInstallDirectory(packageId, version);
        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);
        if (prepared.CaptureState(installed) != prepared.ExpectedState)
        {
            throw new WorkloadPlanChangedException();
        }
        WorkloadEntry? existing = installed.FirstOrDefault(entry =>
            string.Equals(entry.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.PackageVersion, version, StringComparison.Ordinal));
        WorkloadEntry? currentLogicalOwner = ResolveCurrentLogicalOwner(installed, ownership, logicalPackage);
        bool logicalOwnerMoves = LogicalOwnerMoves(currentLogicalOwner, packageId, version);
        WorkloadOwnershipRules.ValidateDestination(existing, logicalPackage, ownership);
        RejectBundledMutation(existing, prepared.Force);
        RejectBundledMutation(currentLogicalOwner, logicalOwnerMoves);
        if (logicalOwnerMoves && prepared.BundledTemplates is not null)
        {
            throw new InvalidWorkloadException("Moving logical ownership to bundled content is not supported in this fresh-install phase. Nothing was changed.");
        }
        if (existing is not null && HasBundledTemplates(existing) && prepared.BundledTemplates is null)
        {
            throw new InvalidWorkloadException("An undeclared artifact cannot complete or replace an installed bundled stack. Retry with its original declared artifact.");
        }

        bool existingValid = !prepared.Force && existing is not null && IsExistingInstallationValid(existing, package.Metadata);
        if (existing is not null && HasBundledTemplates(existing) && !existingValid)
        {
            throw new InvalidWorkloadException("The retained bundled workload payload or manifest is damaged. " +
                "Replacement is not supported; preserve the installation and request explicit repair.");
        }

        if (prepared.BundledTemplates is not null && existing is not null && !HasBundledTemplates(existing))
        {
            throw new InvalidWorkloadException("Replacing an unbundled stack with bundled content is not supported in this fresh-install phase. Nothing was changed.");
        }

        if (!prepared.Force && existing is not null && existingValid)
        {
            if (prepared.BundledTemplates is not null)
            {
                await ValidatePayloadAsync(prepared, installPath, cancellationToken);
            }

            WorkloadInstallResult attached = await AttachOwnershipAsync(operation, existing, ownership, logicalPackage, currentLogicalOwner, cancellationToken);
            operation.CompleteMutation();
            return attached;
        }

        string stagingPath = prepared.StagingPath;
        bool movedToFinal = false;
        bool registered = false;
        try
        {
            WorkloadEntry incoming = CreateEntry(package, prepared.Source, ownership, logicalPackage, prepared.BundledTemplates is not null);
            WorkloadEntry entry = existing is null
                ? incoming
                : MergeForReinstall(existing, incoming, ownership);

            cancellationToken.ThrowIfCancellationRequested();
            operation.BeginMutation();
            DeleteInstallDirectory(installPath);
            Directory.CreateDirectory(Path.GetDirectoryName(installPath)!);
            MoveDirectory(stagingPath, installPath);
            movedToFinal = true;
            progress?.Report(new WorkloadInstallProgress(
                WorkloadInstallPhase.Registering,
                $"Registering workload '{packageId}' {version}"));

            if (logicalOwnerMoves)
            {
                WorkloadOwnershipMoveResult moved =
                    await _store.MoveLogicalOwnershipAsync(operation, currentLogicalOwner!.PackageId, currentLogicalOwner.PackageVersion, entry, cancellationToken);
                registered = true;
                if (moved.PreviousEntryRemoved)
                {
                    TryDeleteDirectory(_paths.GetInstallDirectory(currentLogicalOwner.PackageId, currentLogicalOwner.PackageVersion));
                }

                await ValidatePayloadAsync(prepared, installPath, CancellationToken.None);
                operation.CompleteMutation();
                return new WorkloadInstallResult(moved.Entry, false);
            }

            await _store.SaveWorkloadAsync(operation, entry, cancellationToken);
            registered = true;
            await ValidatePayloadAsync(prepared, installPath, CancellationToken.None);
            operation.CompleteMutation();
            return new WorkloadInstallResult(entry, false);
        }
        catch
        {
            TryDeleteDirectory(stagingPath);
            if (movedToFinal && existing is null && !registered && await IsUnregisteredAsync(packageId, version))
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
        using PreparedWorkloadDeployment prepared = await PrepareAsync(package, source, ownership, logicalPackage, force: true, progress,
            cancellationToken, currentEntry);
        using IWorkloadOperation operation = await _coordinator.AcquireAsync(cancellationToken);
        return await UpdatePreparedAsync(operation, currentEntry, prepared, cancellationToken);
    }

    public async Task<WorkloadEntry> UpdatePreparedAsync(IWorkloadOperation operation, WorkloadEntry currentEntry,
        PreparedWorkloadDeployment prepared, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        await ValidateCurrentEntryAsync(operation, currentEntry, cancellationToken);
        if (prepared.BundledTemplates is null)
        {
            RejectBundledMutation(currentEntry, true);
            return (await UpdatePreparedCoreAsync(operation, currentEntry, prepared, cancellationToken)).Entry;
        }

        PreparedWorkloadReuse previous = prepared.UpdateFrom
            ?? throw new InvalidWorkloadException("An explicit update must prepare the incumbent payload before replacing bundled templates. Nothing was changed.");
        if (!WorkloadStateSnapshot.Same(previous.Entry, currentEntry)
            || prepared.BundledTemplates.Replacing != previous.BundledTemplates
            || prepared.BundledTemplates.RetainPrevious != (currentEntry.InstallRefCount > 1))
        {
            throw new WorkloadPlanChangedException();
        }
        await ValidateReuseAsync(operation, previous, cancellationToken);
        string finalPath = _paths.GetInstallDirectory(prepared.Package.Identity.PackageId, prepared.Package.Identity.Version);
        if (prepared.Destination is not null)
        {
            await ValidateReuseAsync(operation, prepared.Destination, cancellationToken);
        }
        else if (_fileSystem.DirectoryExists(finalPath))
        {
            throw new InvalidWorkloadException("The update destination already exists. No existing workload payload or template ownership was replaced.");
        }
        string registry = await _fileSystem.ReadAllTextIfExistsAsync(_paths.WorkloadRegistryPath, cancellationToken)
            ?? throw new WorkloadPlanChangedException();

        bool previousEntryRemoved = false;
        WorkloadInstallResult result = await _bundledTemplates.CommitAsync(prepared.BundledTemplates, async token =>
        {
            WorkloadOwnershipMoveResult moved = await UpdatePreparedCoreAsync(operation, currentEntry, prepared, token, deferCompletion: true);
            previousEntryRemoved = moved.PreviousEntryRemoved;
            return new(moved.Entry, false);
        }, cancellationToken, async token =>
        {
            if (!operation.IsDirty)
            {
                return;
            }

            await _fileSystem.WriteAllTextAsync(_paths.WorkloadRegistryPath, registry, token);
            if (prepared.Destination is null && _fileSystem.DirectoryExists(finalPath))
            {
                _fileSystem.DeleteDirectory(finalPath);
            }
            operation.CompleteMutation();
        });

        if (previousEntryRemoved)
        {
            string oldPath = _paths.GetInstallDirectory(currentEntry.PackageId, currentEntry.PackageVersion);
            if (!string.Equals(oldPath, finalPath, StringComparison.Ordinal))
            {
                TryDeleteDirectory(oldPath);
            }
        }
        operation.CompleteMutation();
        return result.Entry;
    }

    private async Task<WorkloadOwnershipMoveResult> UpdatePreparedCoreAsync(IWorkloadOperation operation, WorkloadEntry currentEntry,
        PreparedWorkloadDeployment prepared, CancellationToken cancellationToken, bool deferCompletion = false)
    {
        await ValidateCurrentEntryAsync(operation, currentEntry, cancellationToken);
        await ValidatePreparedAsync(prepared, cancellationToken);
        InspectedWorkloadPackage package = prepared.Package;
        IReadOnlyList<WorkloadEntry> installed = await _store.GetWorkloadsAsync(cancellationToken);
        if (prepared.CaptureState(installed) != prepared.ExpectedState)
        {
            throw new WorkloadPlanChangedException();
        }

        WorkloadEntry? destination = installed.SingleOrDefault(entry =>
            string.Equals(entry.PackageId, package.Identity.PackageId, StringComparison.OrdinalIgnoreCase)
            && entry.PackageVersion == package.Identity.Version);
        WorkloadOwnershipRules.ValidateDestination(destination, prepared.LogicalPackage, prepared.Ownership);
        RejectBundledMutation(destination, prepared.Destination is null);
        string finalPath = _paths.GetInstallDirectory(package.Identity.PackageId, package.Identity.Version);
        string stagingPath = prepared.StagingPath;
        try
        {
            WorkloadEntry incoming = CreateEntry(package, prepared.Source, prepared.Ownership, prepared.LogicalPackage, prepared.BundledTemplates is not null);
            if (prepared.Destination is not null)
            {
                await ValidatePayloadAsync(prepared, finalPath, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            operation.BeginMutation();
            if (prepared.Destination is null)
            {
                DeleteInstallDirectory(finalPath);
                Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
                MoveDirectory(stagingPath, finalPath);
            }
            WorkloadOwnershipMoveResult moved = prepared.Ownership == WorkloadOwnershipKind.Logical
                ? await _store.MoveLogicalOwnershipAsync(operation, currentEntry.PackageId, currentEntry.PackageVersion, incoming, cancellationToken)
                : await _store.MoveExplicitOwnershipAsync(operation, currentEntry.PackageId, currentEntry.PackageVersion, incoming, cancellationToken);

            if (moved.PreviousEntryRemoved && !deferCompletion)
            {
                string oldPath = _paths.GetInstallDirectory(currentEntry.PackageId, currentEntry.PackageVersion);
                if (!string.Equals(oldPath, finalPath, StringComparison.Ordinal))
                {
                    TryDeleteDirectory(oldPath);
                }
            }

            await ValidatePayloadAsync(prepared, finalPath, CancellationToken.None);
            if (!deferCompletion)
            {
                operation.CompleteMutation();
            }
            return moved;
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
        using IWorkloadOperation operation = await _coordinator.AcquireAsync(cancellationToken);
        return await UninstallAsync(operation, packageId, version, ownership, cancellationToken);
    }

    public async Task<bool> UninstallAsync(IWorkloadOperation operation, string packageId, string version,
        WorkloadOwnershipKind ownership, CancellationToken cancellationToken)
    {
        ValidateOperation(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (!Enum.IsDefined(ownership))
        {
            throw new ArgumentOutOfRangeException(nameof(ownership));
        }

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

        WorkloadEntry? current = (await _store.GetWorkloadsAsync(cancellationToken)).SingleOrDefault(entry =>
            string.Equals(entry.PackageId, physicalPackageId, StringComparison.OrdinalIgnoreCase)
            && entry.PackageVersion == physicalVersion);
        if (current is null || !HasOwnership(current, ownership))
        {
            return false;
        }
        if (HasBundledTemplates(current))
        {
            return await UninstallBundledAsync(operation, current, ownership, cancellationToken);
        }

        WorkloadOwnershipRemovalResult result = await _store.RemoveOwnershipAsync(operation, physicalPackageId, physicalVersion, ownership, cancellationToken);
        if (!result.OwnershipRemoved)
        {
            return false;
        }

        if (result.EntryRemoved)
        {
            DeleteInstallDirectory(_paths.GetInstallDirectory(physicalPackageId, physicalVersion));
        }

        operation.CompleteMutation();
        return true;
    }

    private async Task<bool> UninstallBundledAsync(IWorkloadOperation operation, WorkloadEntry current,
        WorkloadOwnershipKind ownership, CancellationToken cancellationToken)
    {
        using PreparedWorkloadReuse prepared = await PrepareReuseAsync(current, cancellationToken);
        await ValidateReuseAsync(operation, prepared, cancellationToken, checkOnly: true);
        if (current.InstallRefCount > 1)
        {
            WorkloadOwnershipRemovalResult detached = await _store.RemoveOwnershipAsync(operation, current.PackageId, current.PackageVersion,
                ownership, cancellationToken);
            if (!detached.OwnershipRemoved || detached.EntryRemoved)
            {
                throw new WorkloadPlanChangedException();
            }
            operation.CompleteMutation();
            return true;
        }

        string registry = await _fileSystem.ReadAllTextIfExistsAsync(_paths.WorkloadRegistryPath, cancellationToken)
            ?? throw new WorkloadPlanChangedException();
        await _bundledTemplates.RemoveAsync(prepared.BundledTemplates!, async token =>
        {
            await ValidateReuseAsync(operation, prepared, token);
            WorkloadOwnershipRemovalResult removed = await _store.RemoveOwnershipAsync(operation, current.PackageId, current.PackageVersion,
                ownership, token);
            if (!removed.OwnershipRemoved || !removed.EntryRemoved
                || (await _store.GetWorkloadsAsync(CancellationToken.None)).Any(entry =>
                    string.Equals(entry.PackageId, current.PackageId, StringComparison.OrdinalIgnoreCase)
                    && entry.PackageVersion == current.PackageVersion))
            {
                throw new WorkloadPlanChangedException();
            }
        }, async token =>
        {
            if (operation.IsDirty)
            {
                await _fileSystem.WriteAllTextAsync(_paths.WorkloadRegistryPath, registry, token);
                operation.CompleteMutation();
            }
        }, cancellationToken);

        string path = _paths.GetInstallDirectory(current.PackageId, current.PackageVersion);
        try
        {
            // Native removal and registry admission have committed; finish payload cleanup even after cancellation.
            DeleteInstallDirectory(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidWorkloadException($"Workload '{current.PackageId}' {current.PackageVersion} ownership and native templates were removed, " +
                $"but its payload at '{path}' could not be deleted. Preserve the workload operation intent and reconcile the remaining payload before retrying.",
                exception);
        }
        operation.CompleteMutation();
        return true;
    }

    private async Task<WorkloadInstallResult?> TryReuseImplementationCoreAsync(IWorkloadOperation operation, string packageId, string version, string runtimeIdentifier,
        LogicalPackage logicalPackage, WorkloadEntry? currentLogicalOwner, CancellationToken cancellationToken)
    {
        ValidateOperation(operation);
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

        WorkloadOwnershipRules.ValidateDestination(existing, logicalPackage, WorkloadOwnershipKind.Logical);
        currentLogicalOwner ??= ResolveCurrentLogicalOwner(installed, WorkloadOwnershipKind.Logical, logicalPackage);
        return await AttachOwnershipAsync(operation, existing, WorkloadOwnershipKind.Logical, logicalPackage, currentLogicalOwner, cancellationToken);
    }

    private async Task<WorkloadInstallResult> AttachOwnershipAsync(IWorkloadOperation operation, WorkloadEntry existing, WorkloadOwnershipKind ownership,
        LogicalPackage? logicalPackage, WorkloadEntry? currentLogicalOwner, CancellationToken cancellationToken)
    {
        if (ownership == WorkloadOwnershipKind.Logical && existing.LogicalPackage is null && HasBundledTemplates(existing))
        {
            throw new InvalidWorkloadException("Attaching a new logical owner to an installed bundled stack is not supported yet. Nothing was changed.");
        }

        if (LogicalOwnerMoves(currentLogicalOwner, existing.PackageId, existing.PackageVersion))
        {
            RejectBundledMutation(currentLogicalOwner, true);
            RejectBundledMutation(existing, true);
            WorkloadEntry incoming = CopyForOwnership(existing, WorkloadOwnershipKind.Logical, logicalPackage);
            WorkloadOwnershipMoveResult moved =
                await _store.MoveLogicalOwnershipAsync(operation, currentLogicalOwner!.PackageId, currentLogicalOwner.PackageVersion, incoming, cancellationToken);
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
            await _store.AddOwnershipAsync(operation, CopyForOwnership(existing, ownership, logicalPackage), ownership, cancellationToken);
        return new WorkloadInstallResult(attached, false);
    }

    private void ValidateOperation(IWorkloadOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        operation.Validate(_paths);
    }

    private void RejectBundledMutation(WorkloadEntry? entry, bool destructive)
    {
        if (destructive && entry is not null && HasBundledTemplates(entry))
        {
            throw new InvalidWorkloadException("Bundled stack replacement is not supported yet. Preserve the existing installation; no ownership or payload was changed.");
        }
    }

    private async Task<bool> IsUnregisteredAsync(string packageId, string version)
    {
        try
        {
            IReadOnlyList<WorkloadEntry> entries = await _store.GetWorkloadsAsync(CancellationToken.None);
            return !entries.Any(entry => string.Equals(entry.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
                && entry.PackageVersion == version);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or GracefulException)
        {
            // Failed cleanup inspection must preserve the payload and original failure. The dirty intent blocks subsequent writers.
            return false;
        }
    }

    private async Task ValidateCurrentEntryAsync(IWorkloadOperation operation, WorkloadEntry currentEntry, CancellationToken cancellationToken)
    {
        ValidateOperation(operation);
        ArgumentNullException.ThrowIfNull(currentEntry);
        WorkloadEntry? actual = (await _store.GetWorkloadsAsync(cancellationToken)).SingleOrDefault(entry =>
            string.Equals(entry.PackageId, currentEntry.PackageId, StringComparison.OrdinalIgnoreCase)
            && entry.PackageVersion == currentEntry.PackageVersion);
        if (actual is null || !WorkloadStateSnapshot.Same(actual, currentEntry))
        {
            throw new WorkloadPlanChangedException();
        }
    }

    private Task ValidatePreparedAsync(PreparedWorkloadDeployment prepared, CancellationToken cancellationToken)
        => ValidatePayloadAsync(prepared, prepared.StagingPath, cancellationToken);

    private async Task ValidatePayloadAsync(PreparedWorkloadDeployment prepared, string directory, CancellationToken cancellationToken)
        => await ValidateHashesAsync(prepared.Hashes, directory, cancellationToken);

    private async Task ValidateHashesAsync(IReadOnlyDictionary<string, string> hashes, string directory, CancellationToken cancellationToken)
    {
        if (!_fileSystem.DirectoryExists(directory))
        {
            throw new WorkloadPlanChangedException();
        }

        IReadOnlyList<string> files = _fileSystem.GetFiles(directory);
        if (files.Count != hashes.Count)
        {
            throw new WorkloadPlanChangedException();
        }

        foreach (string file in files)
        {
            if (!hashes.TryGetValue(Path.GetRelativePath(directory, file), out string? expected)
                || await _fileSystem.ComputeSha256Async(file, cancellationToken) != expected)
            {
                throw new WorkloadPlanChangedException();
            }
        }
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

    private static WorkloadEntry CreateEntry(InspectedWorkloadPackage package, string source, WorkloadOwnershipKind ownership,
        LogicalPackage? logicalPackage, bool bundledTemplatesDeclared)
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
            BundledTemplatesDeclared = bundledTemplatesDeclared,
            IsImplicitlyInstalled = ownership != WorkloadOwnershipKind.Explicit,
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
            BundledTemplatesDeclared = entry.BundledTemplatesDeclared,
            IsImplicitlyInstalled = ownership != WorkloadOwnershipKind.Explicit && entry.IsImplicitlyInstalled,
            LogicalPackage = entry.LogicalPackage ?? logicalPackage,
            InstallRefCount = entry.InstallRefCount,
        };

    private static WorkloadEntry MergeForReinstall(WorkloadEntry existing, WorkloadEntry incoming, WorkloadOwnershipKind ownership)
    {
        bool addExplicit = ownership == WorkloadOwnershipKind.Explicit && existing.IsImplicitlyInstalled;
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
            BundledTemplatesDeclared = incoming.BundledTemplatesDeclared,
            IsImplicitlyInstalled = existing.IsImplicitlyInstalled && incoming.IsImplicitlyInstalled,
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
            ? !entry.IsImplicitlyInstalled
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
        => IsMetadataFile(packageFile) || IsPayloadFile(packageFile) || packageFile == BundledTemplateInstaller.ManifestName;

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

    private void DeleteInstallDirectory(string path)
    {
        if (!_fileSystem.DirectoryExists(path))
        {
            return;
        }

        _fileSystem.DeleteDirectory(path);

        // Windows can report a successful delete while the directory lingers until the last handle closes.
        if (_fileSystem.DirectoryExists(path))
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
