// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Filesystem-backed <see cref="IWorkloadStore"/>. Persists the registry as
/// JSON at <see cref="IWorkloadPaths.WorkloadRegistryPath"/>.
/// </summary>
internal class WorkloadStore(IWorkloadPaths paths) : IWorkloadStore
{
    private readonly IWorkloadPaths _paths = paths
        ?? throw new ArgumentNullException(nameof(paths));

    public async Task<IReadOnlyList<WorkloadEntry>> GetWorkloadsAsync(CancellationToken cancellationToken = default)
    {
        WorkloadRegistry registry = await ReadRegistryAsync(cancellationToken);
        return [.. registry.Workloads];
    }

    public async Task SaveWorkloadAsync(WorkloadEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.PackageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.PackageVersion);

        WorkloadRegistry registry = await ReadRegistryAsync(cancellationToken);

        int existing = FindIndex(registry, entry.PackageId, entry.PackageVersion);
        if (existing >= 0)
        {
            registry.Workloads[existing] = entry;
        }
        else
        {
            registry.Workloads.Add(entry);
        }

        await WriteRegistryAsync(registry, cancellationToken);
    }

    public async Task<bool> RemoveWorkloadAsync(string packageId, string version, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        WorkloadRegistry registry = await ReadRegistryAsync(cancellationToken);

        int index = FindIndex(registry, packageId, version);
        if (index < 0)
        {
            return false;
        }

        registry.Workloads.RemoveAt(index);
        await WriteRegistryAsync(registry, cancellationToken);
        return true;
    }

    public async Task ReplaceWorkloadAsync(string oldPackageId, string oldVersion, WorkloadEntry newEntry,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldPackageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldVersion);
        ArgumentNullException.ThrowIfNull(newEntry);
        ArgumentException.ThrowIfNullOrWhiteSpace(newEntry.PackageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newEntry.PackageVersion);

        WorkloadRegistry registry = await ReadRegistryAsync(cancellationToken);

        int oldIndex = FindIndex(registry, oldPackageId, oldVersion);
        if (oldIndex >= 0)
        {
            registry.Workloads.RemoveAt(oldIndex);
        }

        int newIndex = FindIndex(registry, newEntry.PackageId, newEntry.PackageVersion);
        if (newIndex >= 0)
        {
            registry.Workloads[newIndex] = newEntry;
        }
        else
        {
            registry.Workloads.Add(newEntry);
        }

        await WriteRegistryAsync(registry, cancellationToken);
    }

    public async Task<WorkloadEntry> AddOwnershipAsync(WorkloadEntry entry, WorkloadOwnershipKind ownership,
        CancellationToken cancellationToken = default)
    {
        ValidateEntry(entry);
        ValidateOwnership(entry, ownership);

        WorkloadRegistry registry = await ReadRegistryAsync(cancellationToken);
        WorkloadEntry merged = AddOwnershipCore(registry, entry, ownership);
        await WriteRegistryAsync(registry, cancellationToken);
        return merged;
    }

    public async Task<WorkloadOwnershipRemovalResult> RemoveOwnershipAsync(string packageId, string version,
        WorkloadOwnershipKind ownership, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        WorkloadRegistry registry = await ReadRegistryAsync(cancellationToken);
        WorkloadOwnershipRemovalResult result = RemoveOwnershipCore(registry, packageId, version, ownership);
        if (!result.OwnershipRemoved)
        {
            return result;
        }

        await WriteRegistryAsync(registry, cancellationToken);
        return result;
    }

    public async Task<WorkloadOwnershipMoveResult> MoveLogicalOwnershipAsync(string oldPackageId, string oldVersion,
        WorkloadEntry newEntry, CancellationToken cancellationToken = default)
        => await MoveOwnershipAsync(oldPackageId, oldVersion, newEntry, WorkloadOwnershipKind.Logical, cancellationToken);

    public async Task<WorkloadOwnershipMoveResult> MoveExplicitOwnershipAsync(string oldPackageId, string oldVersion,
        WorkloadEntry newEntry, CancellationToken cancellationToken = default)
        => await MoveOwnershipAsync(oldPackageId, oldVersion, newEntry, WorkloadOwnershipKind.Explicit, cancellationToken);

    private async Task<WorkloadOwnershipMoveResult> MoveOwnershipAsync(string oldPackageId, string oldVersion,
        WorkloadEntry newEntry, WorkloadOwnershipKind ownership, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldPackageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldVersion);
        ValidateEntry(newEntry);
        ValidateOwnership(newEntry, ownership);

        WorkloadRegistry registry = await ReadRegistryAsync(cancellationToken);
        WorkloadOwnershipRemovalResult removal = RemoveOwnershipCore(registry, oldPackageId, oldVersion, ownership);
        if (!removal.OwnershipRemoved)
        {
            throw new InvalidOperationException(
                $"{ownership} ownership for workload '{oldPackageId}' version '{oldVersion}' is not installed.");
        }

        WorkloadEntry merged = AddOwnershipCore(registry, newEntry, ownership);
        await WriteRegistryAsync(registry, cancellationToken);
        return new WorkloadOwnershipMoveResult(merged, removal.EntryRemoved);
    }

    private static WorkloadEntry AddOwnershipCore(WorkloadRegistry registry, WorkloadEntry entry, WorkloadOwnershipKind ownership)
    {
        int index = FindIndex(registry, entry.PackageId, entry.PackageVersion);
        WorkloadEntry merged = index < 0
            ? NormalizeNewEntry(entry, ownership)
            : MergeOwnership(registry.Workloads[index], entry, ownership);

        if (index < 0)
        {
            registry.Workloads.Add(merged);
        }
        else
        {
            registry.Workloads[index] = merged;
        }

        return merged;
    }

    private static WorkloadOwnershipRemovalResult RemoveOwnershipCore(
        WorkloadRegistry registry, string packageId, string version, WorkloadOwnershipKind ownership)
    {
        int index = FindIndex(registry, packageId, version);
        if (index < 0)
        {
            return new WorkloadOwnershipRemovalResult(false, false, null);
        }

        WorkloadEntry current = registry.Workloads[index];
        bool hasOwnership = ownership switch
        {
            WorkloadOwnershipKind.Explicit => !current.IsImplicitlyInstalled,
            WorkloadOwnershipKind.Logical => current.LogicalPackage is not null,
            _ => throw new ArgumentOutOfRangeException(nameof(ownership)),
        };

        if (!hasOwnership)
        {
            return new WorkloadOwnershipRemovalResult(false, false, current);
        }

        int remainingReferences = current.InstallRefCount - 1;
        if (remainingReferences <= 0)
        {
            registry.Workloads.RemoveAt(index);
            return new WorkloadOwnershipRemovalResult(true, true, null);
        }

        WorkloadEntry updated = CopyWithOwnership(
            current, isImplicitlyInstalled: ownership == WorkloadOwnershipKind.Explicit || current.IsImplicitlyInstalled,
            logicalPackage: ownership == WorkloadOwnershipKind.Logical ? null : current.LogicalPackage, installRefCount: remainingReferences);
        registry.Workloads[index] = updated;
        return new WorkloadOwnershipRemovalResult(true, false, updated);
    }

    private static int FindIndex(WorkloadRegistry registry, string packageId, string version)
    {
        for (int i = 0; i < registry.Workloads.Count; i++)
        {
            WorkloadEntry candidate = registry.Workloads[i];
            if (string.Equals(candidate.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.PackageVersion, version, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static void ValidateEntry(WorkloadEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.PackageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.PackageVersion);
    }

    private static void ValidateOwnership(WorkloadEntry entry, WorkloadOwnershipKind ownership)
    {
        if (ownership == WorkloadOwnershipKind.Logical && entry.LogicalPackage is null)
        {
            throw new ArgumentException("Logical ownership requires logical package metadata.", nameof(entry));
        }
    }

    private static WorkloadEntry NormalizeNewEntry(WorkloadEntry entry, WorkloadOwnershipKind ownership)
    {
        bool explicitOwner = ownership == WorkloadOwnershipKind.Explicit || !entry.IsImplicitlyInstalled;
        int ownerCount = (explicitOwner ? 1 : 0) + (entry.LogicalPackage is null ? 0 : 1);
        return CopyWithOwnership(entry, !explicitOwner, entry.LogicalPackage, Math.Max(ownerCount, entry.InstallRefCount));
    }

    private static WorkloadEntry MergeOwnership(WorkloadEntry current, WorkloadEntry incoming, WorkloadOwnershipKind ownership)
    {
        bool attachExplicit = ownership == WorkloadOwnershipKind.Explicit && current.IsImplicitlyInstalled;
        bool attachLogical = current.LogicalPackage is null && incoming.LogicalPackage is not null;
        if (ownership == WorkloadOwnershipKind.Logical
            && current.LogicalPackage is not null
            && !SameLogicalOwner(current.LogicalPackage, incoming.LogicalPackage!))
        {
            throw new InvalidOperationException(
                $"Physical workload '{current.PackageId}' version '{current.PackageVersion}' is already owned by logical package " +
                $"'{current.LogicalPackage.PackageId}' version '{current.LogicalPackage.PackageVersion}'.");
        }

        // Payload metadata comes from the incoming entry: the caller may have just replaced the payload
        // on disk, and a legacy row would otherwise keep stale values such as a missing runtime identifier.
        return CopyWithOwnership(
            incoming,
            current.IsImplicitlyInstalled && ownership != WorkloadOwnershipKind.Explicit,
            current.LogicalPackage ?? incoming.LogicalPackage,
            current.InstallRefCount + (attachExplicit ? 1 : 0) + (attachLogical ? 1 : 0));
    }

    private static bool SameLogicalOwner(LogicalPackage left, LogicalPackage right)
        => string.Equals(left.PackageId, right.PackageId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.PackageVersion, right.PackageVersion, StringComparison.Ordinal);

    private static WorkloadEntry CopyWithOwnership(
        WorkloadEntry entry, bool isImplicitlyInstalled, LogicalPackage? logicalPackage, int installRefCount)
        => new()
        {
            PackageId = entry.PackageId,
            PackageVersion = entry.PackageVersion,
            Kind = entry.Kind,
            Aliases = entry.Aliases,
            DisplayName = entry.DisplayName,
            Description = entry.Description,
            Source = entry.Source,
            RuntimeIdentifier = entry.RuntimeIdentifier,
            IsImplicitlyInstalled = isImplicitlyInstalled,
            LogicalPackage = logicalPackage,
            InstallRefCount = installRefCount,
            EntryPoint = entry.EntryPoint,
        };

    private async Task<WorkloadRegistry> ReadRegistryAsync(CancellationToken cancellationToken)
    {
        string path = _paths.WorkloadRegistryPath;
        if (!File.Exists(path))
        {
            return new WorkloadRegistry();
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);

            WorkloadRegistry registry = await JsonSerializer.DeserializeAsync(
                stream,
                WorkloadJsonContext.Default.WorkloadRegistry,
                cancellationToken)
                ?? new WorkloadRegistry();

            if (!WorkloadManifestSchema.IsRegistrySupported(registry.Schema))
            {
                string supported = string.Join(
                    Environment.NewLine,
                    WorkloadManifestSchema.SupportedRegistrySchemas.Select(s => $"  - {s}"));

                throw new GracefulException(
                    $"The schema '{registry.Schema}' declared by registry '{path}' is not supported."
                    + Environment.NewLine
                    + "Supported schemas are:"
                    + Environment.NewLine
                    + supported
                    + Environment.NewLine
                    + Environment.NewLine
                    + "Check for spelling or try updating the CLI to the latest version.",
                    isUserError: true);
            }

            return registry;
        }
        catch (JsonException ex)
        {
            throw new GracefulException(
                $"Failed to parse '{path}': {ex.Message}",
                isUserError: true);
        }
    }

    private async Task WriteRegistryAsync(WorkloadRegistry registry, CancellationToken cancellationToken)
    {
        string path = _paths.WorkloadRegistryPath;
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        // Serialize to a temp file first so a partial write (crash, power loss,
        // disk full, serialization exception) cannot corrupt the existing
        // registry. The subsequent File.Move replaces the target atomically
        // because the temp file lives in the same directory (rename(2) /
        // MoveFileEx with REPLACE_EXISTING are atomic within a filesystem).
        string tempPath = Path.Combine(directory, $"{Guid.NewGuid():N}.json.tmp");

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await SerializeAsync(stream, registry, cancellationToken);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Serialize hook. Tests substitute this to exercise the failure-path
    /// cleanup (temp file removed, original registry preserved).
    /// </summary>
    internal virtual Task SerializeAsync(Stream stream, WorkloadRegistry registry, CancellationToken cancellationToken)
        => JsonSerializer.SerializeAsync(
            stream,
            registry,
            WorkloadJsonContext.Default.WorkloadRegistry,
            cancellationToken);

    private static void TryDelete(string path)
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
            // Best-effort cleanup; swallow so the original exception surfaces.
        }
    }
}
