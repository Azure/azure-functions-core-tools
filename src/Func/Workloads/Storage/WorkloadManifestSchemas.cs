// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Known schema URIs for the on-disk workload manifest.
/// </summary>
/// <remarks>
/// The manifest carries its schema URI in the top-level <c>$schema</c>
/// property. The URI doubles as a version marker — a manifest written by
/// a future Func CLI with a higher schema URI will be rejected at load
/// time with an actionable error so the user updates their CLI rather
/// than silently losing data on a partial parse.
/// </remarks>
internal static class WorkloadManifestSchemas
{
    /// <summary>
    /// Initial schema. Manifests with no <c>$schema</c> field are treated
    /// as v1 for back-compat with manifests written before the field was
    /// introduced; they are re-emitted with the field on the next write.
    /// </summary>
    public const string V1 = "https://aka.ms/func-workloads/v1/schema.json";

    /// <summary>
    /// Returns <c>true</c> if the supplied schema URI is one this CLI
    /// understands. Empty or null is treated as supported (legacy v1).
    /// </summary>
    public static bool IsSupported(string? schema)
        => string.IsNullOrEmpty(schema) || schema == V1;
}
