// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Reads and writes the global workload manifest at <see cref="WorkloadPathsOptions.GlobalManifestPath"/>.
/// </summary>
internal sealed class GlobalManifestStore
{
    private readonly WorkloadPathsOptions _paths;

    public GlobalManifestStore(IOptions<WorkloadPathsOptions> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths.Value;
    }

    /// <summary>
    /// Reads the global manifest, returning an empty one if it doesn't exist.
    /// </summary>
    public GlobalManifest Read()
    {
        var path = _paths.GlobalManifestPath;
        if (!File.Exists(path))
        {
            return new GlobalManifest();
        }

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, WorkloadJsonContext.Default.GlobalManifest)
                ?? new GlobalManifest();
        }
        catch (JsonException ex)
        {
            throw new GracefulException(
                $"Failed to parse '{path}': {ex.Message}",
                isUserError: true);
        }
    }

    /// <summary>
    /// Writes the global manifest, creating the directory if needed.
    /// </summary>
    /// <remarks>
    /// The write is atomic: serialization goes to a temp file in the same
    /// directory and is then renamed over the final path. A crash mid-write
    /// leaves the previous manifest intact (or no manifest at all).
    /// </remarks>
    public void Write(GlobalManifest manifest)
    {
        var path = _paths.GlobalManifestPath;
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        // Serialize to a temp file first so a partial write (crash, power loss,
        // disk full, serialization exception) cannot corrupt the existing
        // manifest. The subsequent File.Move replaces the target atomically.
        var tempPath = Path.Combine(directory, $"{Guid.NewGuid():N}.json.tmp");

        try
        {
            using (var stream = File.Create(tempPath))
            {
                JsonSerializer.Serialize(stream, manifest, WorkloadJsonContext.Default.GlobalManifest);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

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
