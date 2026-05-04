// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Versioning for the on-disk workload manifest, using a JSON Schema URL
/// rather than a numeric version field.
/// </summary>
/// <remarks>
/// The manifest carries its schema identity in the top-level
/// <c>$schema</c> property, the same convention used by widely-supported
/// JSON config formats (e.g. <c>tsconfig.json</c>, <c>azure-pipelines.yml</c>,
/// <c>dotnet/global.json</c>). Editors and validators can fetch the schema
/// at the URL and validate the document; breaking changes ship under a
/// new versioned URL (<c>/v2/</c>, <c>/v3/</c>, ...) so older and newer
/// CLIs can coexist on disk without one silently corrupting the other.
/// A manifest written by a future Func CLI with a schema URL this CLI
/// doesn't recognize is rejected at load time with an actionable error.
/// </remarks>
internal static class WorkloadManifestSchema
{
    /// <summary>
    /// Current schema URL. Bump the version segment (<c>v1</c> → <c>v2</c>)
    /// when the manifest shape changes in a way older CLIs cannot safely read.
    /// </summary>
    public const string CurrentSchema = "https://aka.ms/func-workloads/v1/schema.json";

    /// <summary>
    /// Returns <c>true</c> if this CLI understands the supplied schema URL.
    /// <c>null</c> or empty is treated as legacy v1 so manifests written
    /// before this field was introduced still load.
    /// </summary>
    public static bool IsSupported(string? schema)
        => string.IsNullOrEmpty(schema)
            || string.Equals(schema, CurrentSchema, StringComparison.Ordinal);
}

