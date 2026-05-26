// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Fetches and caches the CDN-hosted template manifest used by
/// <c>func quickstart</c>. Implementations handle ETag caching,
/// TTL refresh, and fallback strategies.
/// </summary>
public interface IQuickstartManifestService
{
    /// <summary>
    /// Returns the current template manifest, fetching from the CDN if the
    /// local cache is stale or absent.
    /// </summary>
    public Task<IReadOnlyList<QuickstartEntry>> GetManifestAsync(CancellationToken cancellationToken = default);
}
