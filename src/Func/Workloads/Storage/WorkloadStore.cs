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

    public async Task<IReadOnlyList<WorkloadEntry>> GetWorkloadsAsync(
        CancellationToken cancellationToken = default)
    {
        WorkloadRegistry registry = await ReadRegistryAsync(cancellationToken);
        return [.. registry.Workloads];
    }

    public async Task SaveWorkloadAsync(
        WorkloadEntry entry,
        CancellationToken cancellationToken = default)
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

    public async Task<bool> RemoveWorkloadAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default)
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

    public async Task ReplaceWorkloadAsync(
        string oldPackageId,
        string oldVersion,
        WorkloadEntry newEntry,
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

    private async Task<WorkloadRegistry> ReadRegistryAsync(CancellationToken cancellationToken)
    {
        string path = _paths.WorkloadRegistryPath;
        if (!File.Exists(path))
        {
            return new WorkloadRegistry();
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

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
    internal virtual Task SerializeAsync(
        Stream stream,
        WorkloadRegistry registry,
        CancellationToken cancellationToken)
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
