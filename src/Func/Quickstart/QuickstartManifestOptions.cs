// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Configuration for the CDN quickstart manifest client.
/// </summary>
internal sealed class QuickstartManifestOptions
{
    /// <summary>
    /// Primary CDN URL for the manifest JSON.
    /// </summary>
    public string ManifestUrl { get; set; } =
        "https://cdn.functions.azure.com/public/templates-manifest/manifest.json";

    /// <summary>
    /// Fallback URL used when the primary CDN is unreachable.
    /// </summary>
    public string BackupManifestUrl { get; set; } =
        "https://raw.githubusercontent.com/Azure/azure-functions-templates/dev/Functions.Templates/Template-Manifest/manifest.json";

    /// <summary>
    /// Directory where the manifest JSON and ETag metadata are cached.
    /// </summary>
    public string CacheDirectory { get; set; } = DefaultCacheDirectory();

    /// <summary>
    /// How long a cached manifest is considered fresh before re-validation.
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(24);

    private static string DefaultCacheDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Constants.FuncHomeDirectoryName,
            "cache",
            "quickstart");
}
