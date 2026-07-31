// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Search;

/// <summary>
/// A package discovered by searching a <c>--source</c> feed directly (i.e. not
/// via the func index). Templates are not enumerated for feed hits — only the
/// package identity is known without downloading and scanning the package.
/// </summary>
/// <param name="PackageId">NuGet package id.</param>
/// <param name="Version">Latest version reported by the feed.</param>
internal sealed record FuncFeedPackage(string PackageId, string Version);

/// <summary>
/// Searches a single NuGet feed for func template packages, filtered to the
/// func package types (<c>FuncItemTemplates</c> / <c>FuncAppTemplates</c>).
/// Handles both remote v3 feeds (search API) and local directory feeds (scan).
/// </summary>
internal interface IFuncTemplateFeedSearch
{
    /// <summary>
    /// Returns func template packages from <paramref name="source"/> matching
    /// <paramref name="term"/> (all packages when the term is empty).
    /// </summary>
    /// <exception cref="InvalidOperationException">The feed is unreachable or not a usable NuGet source.</exception>
    public Task<IReadOnlyList<FuncFeedPackage>> SearchAsync(string? term, string source, CancellationToken cancellationToken);
}
