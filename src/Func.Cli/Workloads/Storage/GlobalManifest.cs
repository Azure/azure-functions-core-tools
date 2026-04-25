// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Aggregated manifest at <c>~/.azure-functions/workloads.json</c>. Lists
/// every workload <c>func workload install</c> has installed. Source of
/// truth for the loader and <c>func workload list</c>.
/// </summary>
internal sealed class GlobalManifest
{
    [JsonPropertyName("workloads")]
    public List<GlobalManifestEntry> Workloads { get; init; } = new();
}

/// <summary>
/// One entry in <see cref="GlobalManifest"/>. A snapshot of the package
/// manifest plus the resolved install location.
/// </summary>
internal sealed class GlobalManifestEntry
{
    [JsonPropertyName("packageId")]
    public required string PackageId { get; init; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter<WorkloadType>))]
    public required WorkloadType Type { get; init; }

    [JsonPropertyName("aliases")]
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();

    /// <summary>Absolute path to the install directory (contains the assembly + workload.json).</summary>
    [JsonPropertyName("installPath")]
    public required string InstallPath { get; init; }

    [JsonPropertyName("entryPoint")]
    public required EntryPointSpec EntryPoint { get; init; }
}

/// <summary>
/// Identifies the type that implements <see cref="IWorkload"/>. Auto-discovered
/// at install time by scanning the package's <c>lib/&lt;tfm&gt;/</c> assemblies.
/// </summary>
internal sealed class EntryPointSpec
{
    /// <summary>Path to the assembly relative to the install directory (e.g. <c>lib/net10.0/Foo.dll</c>).</summary>
    [JsonPropertyName("assembly")]
    public required string Assembly { get; init; }

    /// <summary>Fully-qualified type name implementing <see cref="IWorkload"/>.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }
}
