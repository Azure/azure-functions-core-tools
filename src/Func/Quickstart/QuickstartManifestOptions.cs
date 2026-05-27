// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AbstractionsConstants = Azure.Functions.Cli.Abstractions.Common.Constants;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Configuration for the quickstart manifest service.
/// </summary>
internal sealed class QuickstartManifestOptions
{
    /// <summary>
    /// Primary CDN URL for the manifest JSON.
    /// </summary>
    public string ManifestUrl { get; set; } =
        "https://cdn.functions.azure.com/public/templates-manifest/manifest.json";

    /// <summary>
    /// Directory where the manifest JSON and ETag metadata are cached.
    /// </summary>
    public string CacheDirectory { get; set; } = DefaultCacheDirectory();

    /// <summary>
    /// How long a cached manifest is considered fresh before re-validation.
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// HTTP timeout for fetching the manifest from the CDN.
    /// </summary>
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(15);

    private static string DefaultCacheDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AbstractionsConstants.FuncHomeDirectoryName,
            "quickstart");
}
