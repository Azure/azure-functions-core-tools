// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Contents of a workload's <c>workload.json</c> manifest. Every workload
/// package must ship one at its root; packages without it are not valid
/// workloads and are rejected at install time.
/// </summary>
internal sealed class WorkloadMetadata
{
    /// <summary>
    /// JSON Schema URL identifying the manifest format.
    /// </summary>
    [JsonPropertyName("$schema")]
    [JsonPropertyOrder(-1)]
    public required string Schema { get; init; }

    /// <summary>
    /// Package shape. Defaults to <see cref="WorkloadKind.Workload"/>.
    /// </summary>
    public WorkloadKind Kind { get; init; } = WorkloadKind.Workload;

    /// <summary>
    /// Entry-point assembly and type. Required for
    /// <see cref="WorkloadKind.Workload"/>; otherwise null.
    /// </summary>
    public EntryPointSpec? EntryPoint { get; init; }

    /// <summary>
    /// Maps normalized runtime identifiers to exact implementation package ids.
    /// Required only for <see cref="WorkloadKind.RidPointer"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Packages { get; init; }

    /// <summary>
    /// Runtime identifier of a RID-specific workload or content implementation.
    /// </summary>
    public string? RuntimeIdentifier { get; init; }

    /// <summary>
    /// Optional template directory relative to the physical package root.
    /// Only runtime workloads may declare it.
    /// </summary>
    public string? TemplateRoot { get; set; }

    /// <summary>
    /// Human-readable name for inventory and list output.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// One-line workload description for inventory and list output.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Normalizes a relative directory path and verifies it resolves inside the package root.
    /// </summary>
    public static string NormalizePackagePath(string path, string packageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);

        string relativePath = path.Replace('\\', '/');
        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("The path must be relative to the package root.", nameof(path));
        }

        string packageRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageDirectory));
        string resolvedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(packageRoot, relativePath)));
        string packageRootPrefix = Path.EndsInDirectorySeparator(packageRoot) ? packageRoot : packageRoot + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!resolvedPath.StartsWith(packageRootPrefix, comparison)
            || string.Equals(resolvedPath, packageRoot, comparison))
        {
            throw new ArgumentException("The path must resolve to a directory inside the package root.", nameof(path));
        }

        return Path.GetRelativePath(packageRoot, resolvedPath).Replace('\\', '/');
    }
}
