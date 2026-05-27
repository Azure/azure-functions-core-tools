// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Cached manifest metadata: ETag and timestamp of last successful fetch.
/// </summary>
internal sealed record ManifestCacheMeta(string ETag, DateTimeOffset CachedAt);
