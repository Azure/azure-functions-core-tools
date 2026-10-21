// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Update;

/// <summary>
/// Configures the CDN used to resolve and download Azure Functions CLI releases.
/// </summary>
internal sealed class UpdateCdnOptions
{
    /// <summary>
    /// Gets or sets the absolute CDN base URL.
    /// </summary>
    public string BaseUrl { get; set; } = "https://cdn.functions.azure.com/";

    /// <summary>
    /// Gets or sets the timeout for release metadata and artifact requests.
    /// </summary>
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
