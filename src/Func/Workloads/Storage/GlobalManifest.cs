// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// On-disk shape of <c>~/.azure-functions/workloads.json</c>. Two-level
/// nested dictionary: outer keyed by package id (case-insensitive), inner
/// keyed by package version. The structural shape is what gives us
/// uniqueness — there is no way to record two entries for the same
/// (package id, version) pair, by construction.
/// </summary>
/// <remarks>
/// The outer dictionary is constructed with
/// <see cref="StringComparer.OrdinalIgnoreCase"/> so package-id lookups
/// work regardless of how the caller cases the id (NuGet ids are
/// case-insensitive). On read, the comparer is reapplied because
/// <see cref="System.Text.Json.JsonSerializer"/> deserializes dictionaries
/// with the default ordinal comparer.
/// </remarks>
internal sealed class GlobalManifest
{
    /// <summary>
    /// Schema URI for this manifest. Doubles as a version marker so a
    /// future CLI with a higher schema can reject manifests written by
    /// an older CLI it doesn't know how to read (and vice versa).
    /// </summary>
    [JsonPropertyName("$schema")]
    [JsonPropertyOrder(-1)]
    public string Schema { get; init; } = WorkloadManifestSchemas.V1;

    /// <summary>
    /// Installed workloads indexed by package id, then by version. Empty
    /// when no workload has been installed yet.
    /// </summary>
    public Dictionary<string, Dictionary<string, GlobalManifestEntry>> Workloads { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// One installed workload version. The owning package id and version live
/// in the surrounding <see cref="GlobalManifest.Workloads"/> dictionary
/// keys, so they're not duplicated on the entry itself.
/// </summary>
internal sealed class GlobalManifestEntry
{
    /// <summary>
    /// Human-readable name shown by <c>func workload list</c> (e.g. <c>".NET"</c>).
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Optional one-line description shown by <c>func workload list</c>.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Short aliases the user can pass to <c>func workload install/uninstall</c>
    /// instead of the full package id (e.g. <c>"dotnet"</c>).
    /// </summary>
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Absolute path to the install directory (contains the assembly + workload.json).
    /// </summary>
    public required string InstallPath { get; init; }

    /// <summary>
    /// Where to find the <see cref="IWorkload"/> implementation when the loader activates this workload.
    /// </summary>
    public required EntryPointSpec EntryPoint { get; init; }
}

/// <summary>
/// Identifies the type that implements <see cref="IWorkload"/>. Auto-discovered
/// at install time via <c>[assembly: ExportCliWorkload&lt;T&gt;]</c>.
/// </summary>
internal sealed class EntryPointSpec
{
    /// <summary>
    /// Path to the assembly relative to the install directory (e.g. <c>lib/net10.0/Foo.dll</c>).
    /// </summary>
    public required string Assembly { get; init; }

    /// <summary>
    /// Fully-qualified type name implementing <see cref="IWorkload"/>. Stored as a string so the manifest
    /// stays loadable even when the assembly isn't on the runtime probe path (e.g. listing workloads
    /// without loading them).
    /// </summary>
    public required string Type { get; init; }
}

/// <summary>
/// Projection of a single (package id, version, entry) triple from the
/// nested manifest. Lets callers iterate the manifest as a flat list
/// without exposing the on-disk dictionary shape.
/// </summary>
internal sealed record InstalledWorkload(string PackageId, string Version, GlobalManifestEntry Entry);
