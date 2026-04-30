// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Aggregated manifest at <c>~/.azure-functions/workloads.json</c>. Lists
/// every workload <c>func workload install</c> has installed. Source of
/// truth for the loader and <c>func workload list</c>.
/// </summary>
internal sealed class GlobalManifest
{
    /// <summary>
    /// Every workload currently installed for the user. Order is the order they were installed.
    /// </summary>
    public List<GlobalManifestEntry> Workloads { get; init; } = new();
}

/// <summary>
/// One entry in <see cref="GlobalManifest"/>. Snapshot of the package
/// metadata plus the resolved install location and entry point.
/// </summary>
internal sealed class GlobalManifestEntry
{
    /// <summary>
    /// NuGet package id this workload was installed from (e.g. <c>Azure.Functions.Cli.Workload.Dotnet</c>).
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// Human-readable name shown by <c>func workload list</c> (e.g. <c>".NET"</c>).
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Optional one-line description shown by <c>func workload list</c>.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Installed package version (NuGet version string).
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Short aliases the user can pass to <c>func workload install/uninstall</c> instead of the full package id (e.g. <c>"dotnet"</c>).
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
/// at install time by scanning the package's <c>lib/&lt;tfm&gt;/</c> assemblies.
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
