// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Abstracts filesystem operations for the quickstart manifest cache.
/// </summary>
internal interface IManifestCache
{
    /// <summary>
    /// Ensures the cache directory exists.
    /// </summary>
    public void EnsureDirectory();

    /// <summary>
    /// Reads the cached manifest JSON, or <see langword="null"/> if it doesn't exist or is unreadable.
    /// </summary>
    public string? TryReadManifest();

    /// <summary>
    /// Writes the manifest JSON to the cache.
    /// </summary>
    public void WriteManifest(string json);

    /// <summary>
    /// Reads the cache metadata, or <see langword="null"/> if it doesn't exist or is unreadable.
    /// </summary>
    public ManifestCacheMeta? TryReadMeta();

    /// <summary>
    /// Writes cache metadata.
    /// </summary>
    public void WriteMeta(ManifestCacheMeta meta);

    /// <summary>
    /// Returns <see langword="true"/> if a cached manifest file exists on disk.
    /// </summary>
    public bool ManifestExists();
}
