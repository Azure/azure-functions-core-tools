// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Search;

/// <summary>
/// Resolves and loads the func template search index, honoring the local-file
/// override, the URI override, and the default index URI (with a locally
/// cached copy for the remote path).
/// </summary>
internal interface IFuncSearchIndexProvider
{
    /// <summary>
    /// Loads the search index. A configured local file path resolves fully
    /// offline; otherwise the index is served from the local cache when fresh,
    /// or downloaded (with ETag revalidation and stale-cache fallback).
    /// </summary>
    /// <exception cref="FileNotFoundException">A configured local-file override does not exist.</exception>
    /// <exception cref="FuncSearchIndexFormatException">The resolved index is malformed or an unsupported version.</exception>
    /// <exception cref="InvalidOperationException">The index is unreachable and no cached or local copy exists.</exception>
    public Task<FuncSearchIndex> GetIndexAsync(CancellationToken cancellationToken);
}
