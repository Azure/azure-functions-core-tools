// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Search;

/// <summary>
/// A <c>func new --search</c> query.
/// </summary>
/// <param name="Term">Search term; <c>null</c> or empty lists the whole index.</param>
/// <param name="Source">Optional feed to query directly instead of the index.</param>
internal sealed record FuncSearchRequest(string? Term, string? Source);

/// <summary>
/// Searches for template packages, either over the func-published index or —
/// when a <c>--source</c> feed is supplied — against that feed directly.
/// Results are annotated with installed state.
/// </summary>
internal interface IFuncTemplateSearchService
{
    /// <summary>
    /// Runs the search and returns the matched packages with installed-state
    /// annotation.
    /// </summary>
    /// <exception cref="FileNotFoundException">A configured local-file index override does not exist.</exception>
    /// <exception cref="FuncSearchIndexFormatException">The resolved index is malformed.</exception>
    /// <exception cref="InvalidOperationException">The index / feed is unreachable and no fallback exists.</exception>
    public Task<FuncSearchResults> SearchAsync(FuncSearchRequest request, CancellationToken cancellationToken);
}
