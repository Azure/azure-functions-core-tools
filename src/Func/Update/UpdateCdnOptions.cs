// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Update;

/// <summary>
/// Configuration for the CLI update pipeline. Values default to the
/// production Azure Functions CDN; the base URL can be overridden through
/// <see cref="UpdateRegistration.CdnBaseUrlEnvVar"/> so integration tests
/// and staging feeds can be pointed at without recompiling.
/// </summary>
internal sealed class UpdateCdnOptions
{
    /// <summary>
    /// Base URL used by both the version manifest fetch and artifact
    /// download. Must end with <c>/</c> so relative paths (e.g.
    /// <c>public/cli/v5/version.json</c>) compose correctly.
    /// </summary>
    public string BaseUrl { get; set; } = "https://cdn.functions.azure.com/";

    /// <summary>
    /// HTTP timeout for both manifest fetches and artifact downloads.
    /// Downloads can be large, so this is intentionally generous.
    /// </summary>
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
