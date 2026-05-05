// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// On-disk shape of <c>~/.azure-functions/workloads.json</c>. Flat list of
/// installed workloads keyed by (<see cref="WorkloadEntry.PackageId"/>,
/// <see cref="WorkloadEntry.PackageVersion"/>); side-by-side installs are
/// separate entries.
/// </summary>
internal sealed class WorkloadRegistry
{
    /// <summary>
    /// Installed workloads. Empty when no workload has been installed yet.
    /// </summary>
    public IList<WorkloadEntry> Workloads { get; init; } = new List<WorkloadEntry>();
}

/// <summary>
/// One installed workload version recorded in the global workload registry.
/// </summary>
internal sealed class WorkloadEntry
{
    /// <summary>
    /// NuGet package id (e.g. <c>"Azure.Functions.Cli.Workload.Dotnet"</c>).
    /// Matched case-insensitively (NuGet convention).
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// Installed package version. Matched ordinally.
    /// </summary>
    public required string PackageVersion { get; init; }

    /// <summary>
    /// Short aliases the user can pass to <c>func workload install/uninstall</c>
    /// instead of the full package id (e.g. <c>"dotnet"</c>).
    /// </summary>
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Where to find the <see cref="Workload"/> implementation. Copied from
    /// the package's <see cref="WorkloadMetadata"/> at install time so the
    /// CLI doesn't have to re-read the package on every load.
    /// </summary>
    public required EntryPointSpec EntryPoint { get; init; }
}
