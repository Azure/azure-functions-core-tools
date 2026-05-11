// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Schema URLs used to version the workload subsystem's JSON manifests.
/// </summary>
/// <remarks>
/// Each manifest carries its schema identity in the top-level <c>$schema</c>
/// property. Breaking changes ship under a new versioned URL so older and
/// newer CLIs can coexist on disk. The registry and per-package manifest
/// version independently.
/// </remarks>
internal static class WorkloadManifestSchema
{
    /// <summary>
    /// v1 schema URL for the global workload registry (<c>workloads.json</c>).
    /// </summary>
    public const string RegistryV1Schema = "https://aka.ms/func/workloads/v1/schema.json";

    /// <summary>
    /// v1 schema URL for the per-package manifest (<c>workload.json</c>).
    /// </summary>
    public const string PackageManifestV1Schema = "https://aka.ms/func-workloads/package/v1/schema.json";

    /// <summary>
    /// Current schema URL written into newly-saved registries.
    /// </summary>
    public const string CurrentRegistrySchema = RegistryV1Schema;

    /// <summary>
    /// Current schema URL accepted in package manifests.
    /// </summary>
    public const string CurrentPackageManifestSchema = PackageManifestV1Schema;

    /// <summary>
    /// Registry schemas this CLI knows how to read.
    /// </summary>
    public static IReadOnlyList<string> SupportedRegistrySchemas { get; } = [RegistryV1Schema];

    /// <summary>
    /// Per-package manifest schemas this CLI knows how to read.
    /// </summary>
    public static IReadOnlyList<string> SupportedPackageManifestSchemas { get; } = [PackageManifestV1Schema];

    /// <summary>
    /// Returns <c>true</c> if this CLI understands the supplied registry schema URL.
    /// <c>null</c> or empty is treated as legacy v1 so pre-<c>$schema</c> registries still load.
    /// </summary>
    public static bool IsRegistrySupported(string? schema)
        => string.IsNullOrEmpty(schema) || SupportedRegistrySchemas.Contains(schema, StringComparer.Ordinal);

    /// <summary>
    /// Returns <c>true</c> if this CLI understands the supplied package manifest schema URL.
    /// <c>$schema</c> is required in package manifests, so empty/null is rejected.
    /// </summary>
    public static bool IsPackageManifestSupported(string? schema)
        => !string.IsNullOrEmpty(schema)
           && SupportedPackageManifestSchemas.Contains(schema, StringComparer.Ordinal);
}

