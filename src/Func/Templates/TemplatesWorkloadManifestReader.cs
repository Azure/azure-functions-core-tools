// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Reads the CLI-owned sibling manifest
/// <c>content/templates-workload.json</c> shipped by Node and Python
/// templates content workloads (templates-workload-spec.md §4.4.4, §5.2).
/// </summary>
internal static class TemplatesWorkloadManifestReader
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Returns the manifest's <c>minBundleVersion</c> range (e.g.
    /// <c>"[4.0.0, )"</c>) or <c>null</c> when the file is missing or the
    /// key isn't present. Missing manifest is treated as "no min-bundle
    /// constraint" — the §4.8.2 gate is a no-op in that case.
    /// </summary>
    public static string? GetMinBundleVersion(string installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            throw new ArgumentException("Install directory must be non-empty.", nameof(installDirectory));
        }

        string path = Path.Combine(installDirectory, "tools", "any", "content", "templates-workload.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            ManifestFile? manifest = JsonSerializer.Deserialize<ManifestFile>(stream, _jsonOptions);
            return string.IsNullOrWhiteSpace(manifest?.MinBundleVersion) ? null : manifest.MinBundleVersion;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class ManifestFile
    {
        [System.Text.Json.Serialization.JsonPropertyName("minBundleVersion")]
        public string? MinBundleVersion { get; set; }
    }
}
