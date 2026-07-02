// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Configuration for the remote profile registry source.
/// </summary>
internal sealed class RemoteProfileOptions
{
    /// <summary>
    /// CDN base URL from which the profile registry is fetched.
    /// </summary>
    public Uri CdnBaseUrl { get; set; } = new("https://cdn.functions.azure.com/public/");

    /// <summary>
    /// How long a cached registry is considered fresh before re-fetching.
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// HTTP timeout for fetching the registry from the CDN.
    /// </summary>
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
