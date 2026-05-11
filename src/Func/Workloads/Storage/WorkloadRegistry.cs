// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// On-disk shape of <c>~/.azure-functions/workloads.json</c>.
/// Workload and content packages live in <see cref="Workloads"/> keyed by
/// (<see cref="WorkloadEntry.PackageId"/>, <see cref="WorkloadEntry.PackageVersion"/>);
/// meta packages live in <see cref="Metas"/>, one row per id (no side-by-side).
/// </summary>
internal sealed class WorkloadRegistry
{
    /// <summary>
    /// JSON Schema URL identifying the registry format. A URL this CLI doesn't
    /// recognize is rejected at load.
    /// </summary>
    [JsonPropertyName("$schema")]
    [JsonPropertyOrder(-1)]
    public string Schema { get; init; } = WorkloadManifestSchema.CurrentRegistrySchema;

    /// <summary>
    /// Installed workload and content packages.
    /// </summary>
    public IList<WorkloadEntry> Workloads { get; init; } = [];

    /// <summary>
    /// Installed meta packages. Bookkeeping only; the loader never activates them.
    /// </summary>
    public IList<MetaEntry> Metas { get; init; } = [];
}

/// <summary>
/// One installed workload-or-content package.
/// </summary>
internal sealed class WorkloadEntry
{
    /// <summary>
    /// NuGet package id (e.g. <c>"Azure.Functions.Cli.Workload.Dotnet"</c>).
    /// Matched case-insensitively.
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// Installed package version. Matched ordinally.
    /// </summary>
    public required string PackageVersion { get; init; }

    /// <summary>
    /// Package shape stamped at install time. The loader only activates
    /// <see cref="WorkloadKind.Workload"/>; <see cref="WorkloadKind.Content"/>
    /// ships files only. Meta packages live in <see cref="WorkloadRegistry.Metas"/>.
    /// </summary>
    public WorkloadKind Kind { get; init; } = WorkloadKind.Workload;

    /// <summary>
    /// Short aliases accepted by <c>func workload install/uninstall</c>
    /// (e.g. <c>"dotnet"</c>), captured from the package's <c>.nuspec</c>
    /// <c>alias:&lt;name&gt;</c> tags.
    /// </summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>
    /// Catalog feed URL or local path the package was installed from.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// True when installed directly via <c>func workload install &lt;id&gt;</c>;
    /// false when pulled in as a meta-package member. Defaults to true for legacy entries.
    /// </summary>
    public bool InstalledExplicitly { get; init; } = true;

    /// <summary>
    /// Where to find the <see cref="Workload"/> implementation, copied from
    /// the package's <see cref="WorkloadMetadata"/> at install time.
    /// Required for <see cref="WorkloadKind.Workload"/>; <see langword="null"/>
    /// for <see cref="WorkloadKind.Content"/>.
    /// </summary>
    public EntryPointSpec? EntryPoint { get; init; }
}

/// <summary>
/// One installed meta package. Records which member packages were brought in
/// together so uninstall can offer cascade; metas have no payload of their own.
/// </summary>
internal sealed class MetaEntry
{
    /// <summary>
    /// NuGet package id of the meta. Unique within <see cref="WorkloadRegistry.Metas"/>.
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// Installed meta version.
    /// </summary>
    public required string PackageVersion { get; init; }

    /// <summary>
    /// Catalog feed URL or local path the meta was installed from.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// Member packages this meta brought in, copied from the meta's
    /// <c>.nuspec</c> <c>&lt;dependencies&gt;</c> at install time.
    /// </summary>
    public IReadOnlyList<MetaMember> Members { get; init; } = [];
}

/// <summary>
/// One member of a meta package.
/// </summary>
internal sealed class MetaMember
{
    /// <summary>
    /// NuGet package id of the member.
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// Member version pinned by this meta version.
    /// </summary>
    public required string PackageVersion { get; init; }
}
