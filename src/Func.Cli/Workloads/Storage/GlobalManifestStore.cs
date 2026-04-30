// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Filesystem-backed <see cref="IGlobalManifestStore"/>. Persists the
/// manifest as JSON at <see cref="IWorkloadPaths.GlobalManifestPath"/>.
/// </summary>
internal class GlobalManifestStore(IWorkloadPaths paths) : IGlobalManifestStore
{
    private readonly IWorkloadPaths _paths = paths
        ?? throw new ArgumentNullException(nameof(paths));

    public async Task<IReadOnlyList<GlobalManifestEntry>> GetWorkloadsAsync(
        CancellationToken cancellationToken = default)
    {
        var manifest = await ReadManifestAsync(cancellationToken).ConfigureAwait(false);
        return manifest.Workloads;
    }

    public async Task SaveWorkloadAsync(
        GlobalManifestEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var manifest = await ReadManifestAsync(cancellationToken).ConfigureAwait(false);
        var existing = manifest.Workloads.FindIndex(
            w => string.Equals(w.PackageId, entry.PackageId, StringComparison.OrdinalIgnoreCase));

        if (existing >= 0)
        {
            manifest.Workloads[existing] = entry;
        }
        else
        {
            manifest.Workloads.Add(entry);
        }

        await WriteManifestAsync(manifest, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RemoveWorkloadAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        var manifest = await ReadManifestAsync(cancellationToken).ConfigureAwait(false);
        var removed = manifest.Workloads.RemoveAll(
            w => string.Equals(w.PackageId, packageId, StringComparison.OrdinalIgnoreCase));

        if (removed == 0)
        {
            return false;
        }

        await WriteManifestAsync(manifest, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<GlobalManifest> ReadManifestAsync(CancellationToken cancellationToken)
    {
        var path = _paths.GlobalManifestPath;
        if (!File.Exists(path))
        {
            return new GlobalManifest();
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

            return await JsonSerializer.DeserializeAsync(
                stream,
                WorkloadJsonContext.Default.GlobalManifest,
                cancellationToken).ConfigureAwait(false)
                ?? new GlobalManifest();
        }
        catch (JsonException ex)
        {
            throw new GracefulException(
                $"Failed to parse '{path}': {ex.Message}",
                isUserError: true);
        }
    }

    private async Task WriteManifestAsync(GlobalManifest manifest, CancellationToken cancellationToken)
    {
        var path = _paths.GlobalManifestPath;
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        // Serialize to a temp file first so a partial write (crash, power loss,
        // disk full, serialization exception) cannot corrupt the existing
        // manifest. The subsequent File.Move replaces the target atomically
        // because the temp file lives in the same directory (rename(2) /
        // MoveFileEx with REPLACE_EXISTING are atomic within a filesystem).
        var tempPath = Path.Combine(directory, $"{Guid.NewGuid():N}.json.tmp");

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
                await SerializeAsync(stream, manifest, cancellationToken).ConfigureAwait(false);
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
    /// cleanup (temp file removed, original manifest preserved).
    /// </summary>
    internal virtual Task SerializeAsync(
        Stream stream,
        GlobalManifest manifest,
        CancellationToken cancellationToken)
        => JsonSerializer.SerializeAsync(
            stream,
            manifest,
            WorkloadJsonContext.Default.GlobalManifest,
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
