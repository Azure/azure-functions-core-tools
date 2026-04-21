// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Catalog entry for a workload that can be installed (built-in catalog or
/// discovered third-party). Mirrors the <c>AvailableWorkload</c> record in
/// the in-process branch so command output looks the same.
/// </summary>
public sealed record AvailableWorkload(
    string Id,
    string PackageId,
    string Description,
    string Languages,
    string? InstalledVersion = null)
{
    public bool IsInstalled => InstalledVersion is not null;
}

/// <summary>
/// Built-in catalog of well-known workloads. In a real implementation this is
/// merged with a NuGet search result — for the scaffold we ship just the
/// in-tree sample and the canonical placeholders so the UX matches the
/// in-process branch.
/// </summary>
public static class WorkloadCatalog
{
    public static readonly IReadOnlyList<AvailableWorkload> Entries =
    [
        new("sample", "Azure.Functions.Cli.Workload.Sample", "Sample (in-tree, OOP demo)", "Demo"),
        new("dotnet", "Azure.Functions.Cli.Workload.Dotnet", ".NET (Isolated Worker)", "C#, F#"),
        new("node", "Azure.Functions.Cli.Workload.Node", "Node.js", "JavaScript, TypeScript"),
        new("python", "Azure.Functions.Cli.Workload.Python", "Python", "Python"),
        new("java", "Azure.Functions.Cli.Workload.Java", "Java", "Java"),
        new("powershell", "Azure.Functions.Cli.Workload.PowerShell", "PowerShell", "PowerShell"),
    ];

    public static AvailableWorkload? FindByAlias(string alias) =>
        Entries.FirstOrDefault(w => string.Equals(w.Id, alias, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Record of an installed workload, persisted in <c>installed.json</c>.
/// </summary>
public sealed class InstalledWorkloadInfo
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("packageId")] public string PackageId { get; set; } = string.Empty;
    [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
    [JsonPropertyName("installPath")] public string InstallPath { get; set; } = string.Empty;
    [JsonPropertyName("installedAt")] public DateTimeOffset InstalledAt { get; set; }
}

/// <summary>
/// Aggregate of installed workloads, persisted at
/// <see cref="WorkloadPaths.ManifestPath(string)"/>.
/// </summary>
public sealed class InstalledWorkloadsManifest
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("workloads")] public List<InstalledWorkloadInfo> Workloads { get; set; } = [];
}
