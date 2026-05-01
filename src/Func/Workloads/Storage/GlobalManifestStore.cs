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

    public async Task<IReadOnlyList<InstalledWorkload>> GetWorkloadsAsync(
        CancellationToken cancellationToken = default)
    {
        var manifest = await ReadManifestAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<InstalledWorkload>();
        foreach (var (packageId, versions) in manifest.Workloads)
        {
            foreach (var (version, entry) in versions)
            {
                results.Add(new InstalledWorkload(packageId, version, entry));
            }
        }

        return results;
    }

    public async Task SaveWorkloadAsync(
        string packageId,
        string version,
        GlobalManifestEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(entry);

        var manifest = await ReadManifestAsync(cancellationToken).ConfigureAwait(false);

        if (!manifest.Workloads.TryGetValue(packageId, out var versions))
        {
            versions = new Dictionary<string, GlobalManifestEntry>(StringComparer.Ordinal);
            manifest.Workloads[packageId] = versions;
        }

        versions[version] = entry;

        await WriteManifestAsync(manifest, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RemoveWorkloadAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var manifest = await ReadManifestAsync(cancellationToken).ConfigureAwait(false);

        if (!manifest.Workloads.TryGetValue(packageId, out var versions)
            || !versions.Remove(version))
        {
            return false;
        }

        // Drop the package-id bucket once its last version is gone so the
        // manifest doesn't accumulate empty parents over an install/uninstall
        // cycle.
        if (versions.Count == 0)
        {
            manifest.Workloads.Remove(packageId);
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

            var deserialized = await JsonSerializer.DeserializeAsync(
                stream,
                WorkloadJsonContext.Default.GlobalManifest,
                cancellationToken).ConfigureAwait(false)
                ?? new GlobalManifest();

            // System.Text.Json rebuilds the outer dictionary with the default
            // (case-sensitive) comparer regardless of the in-memory comparer
            // on the type's default. Reapply ordinal-ignore-case so package-id
            // lookups match NuGet semantics across read/write boundaries.
            return RehydrateComparers(deserialized);
        }
        catch (JsonException ex)
        {
            throw new GracefulException(
                $"Failed to parse '{path}': {ex.Message}",
                isUserError: true);
        }
    }

    private static GlobalManifest RehydrateComparers(GlobalManifest manifest)
    {
        if (ReferenceEquals(manifest.Workloads.Comparer, StringComparer.OrdinalIgnoreCase))
        {
            return manifest;
        }

        var rebuilt = new Dictionary<string, Dictionary<string, GlobalManifestEntry>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (packageId, versions) in manifest.Workloads)
        {
            rebuilt[packageId] = versions;
        }

        return new GlobalManifest { Workloads = rebuilt };
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
