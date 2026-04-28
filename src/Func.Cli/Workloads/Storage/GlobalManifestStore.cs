// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Reads and writes the global workload manifest at
/// <see cref="WorkloadPaths.GlobalManifestPath"/>.
/// </summary>
internal static class GlobalManifestStore
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
    };

    /// <summary>Reads the global manifest, returning an empty one if it doesn't exist.</summary>
    public static GlobalManifest Read()
    {
        var path = WorkloadPaths.GlobalManifestPath;
        if (!File.Exists(path))
        {
            return new GlobalManifest();
        }

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<GlobalManifest>(stream, _options) ?? new GlobalManifest();
        }
        catch (JsonException ex)
        {
            throw new GracefulException(
                $"Failed to parse '{path}': {ex.Message}",
                isUserError: true);
        }
    }

    /// <summary>Writes the global manifest, creating the directory if needed.</summary>
    public static void Write(GlobalManifest manifest)
    {
        var path = WorkloadPaths.GlobalManifestPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, manifest, _options);
    }
}
