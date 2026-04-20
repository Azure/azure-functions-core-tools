// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Metadata for an installed workload.
/// </summary>
/// <param name="Id">Unique workload identifier (e.g., "dotnet", "python").</param>
/// <param name="PackageId">The NuGet package ID this workload was installed from.</param>
/// <param name="Version">Installed version string.</param>
/// <param name="InstallPath">Absolute path where the workload is installed.</param>
/// <param name="AssemblyName">The assembly file name to load (e.g., "Func.Workload.Dotnet.dll").</param>
/// <param name="InstalledAt">When this workload was installed.</param>
public record WorkloadInfo(
    string Id,
    string PackageId,
    string Version,
    string InstallPath,
    string AssemblyName,
    DateTimeOffset InstalledAt);

/// <summary>
/// Manifest file that tracks all installed workloads.
/// Stored at ~/.azure-functions/workloads/workloads.json.
/// </summary>
public class WorkloadManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("workloads")]
    public List<WorkloadInfo> Workloads { get; set; } = [];

    /// <summary>
    /// Default path for the workloads directory.
    /// </summary>
    public static string DefaultWorkloadsDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".azure-functions",
            "workloads");

    /// <summary>
    /// Default path for the manifest file.
    /// </summary>
    public static string DefaultManifestPath =>
        Path.Combine(DefaultWorkloadsDirectory, "workloads.json");
}
