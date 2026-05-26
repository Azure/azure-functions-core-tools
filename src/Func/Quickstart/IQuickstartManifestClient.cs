// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Reads the CDN quickstart manifest, applying caching and fallback logic.
/// </summary>
internal interface IQuickstartManifestClient
{
    /// <summary>
    /// Fetches the quickstart manifest, using an ETag-validated cache when available.
    /// Falls back to the backup URL, then the cached copy, on network failures.
    /// </summary>
    public Task<QuickstartManifest> GetManifestAsync(CancellationToken cancellationToken);
}
