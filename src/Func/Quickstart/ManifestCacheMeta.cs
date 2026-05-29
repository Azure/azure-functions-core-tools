// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Cached manifest metadata: ETag, timestamp, and the URL the cache was fetched from.
/// </summary>
/// <remarks>
/// <see cref="SourceUrl"/> is nullable for backward compatibility with cache files
/// written before this field existed; a null value causes the cache to be treated as stale.
/// </remarks>
internal sealed record ManifestCacheMeta(string ETag, DateTimeOffset CachedAt, string? SourceUrl = null);
