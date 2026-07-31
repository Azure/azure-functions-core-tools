// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Abstractions.Common;

namespace Azure.Functions.Cli.Templates.Search;

/// <summary>
/// Configuration for the template search index consumer. Bound from CLI
/// defaults plus the search environment variables at registration time
/// (<see cref="TemplateSearchRegistration"/>); business logic never reads the
/// environment directly.
/// </summary>
internal sealed class FuncTemplateSearchOptions
{
    /// <summary>
    /// Stable vanity URI for the func-published template search index (D29),
    /// redirecting to the Functions CDN. May be overridden with an alternate
    /// URI or a local file path via the search override environment variable.
    /// </summary>
    public const string DefaultIndexUri = "https://aka.ms/func/templates-search/v2";

    /// <summary>
    /// Index location: the default vanity URI, an override URI, or an absolute
    /// local file path / <c>file://</c> URI. A local path resolves fully
    /// offline (no network access).
    /// </summary>
    public string IndexUri { get; set; } = DefaultIndexUri;

    /// <summary>
    /// When true, never download: use only a previously cached copy of the
    /// index (mirrors upstream's local-search-file-only toggle).
    /// </summary>
    public bool LocalOnly { get; set; }

    /// <summary>
    /// Directory where the downloaded index and its cache metadata live —
    /// under the func hive alongside the template engine's own state.
    /// </summary>
    public string CacheDirectory { get; set; } = DefaultCacheDirectory();

    /// <summary>
    /// How long a downloaded index is treated as fresh before re-validation.
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// HTTP timeout for fetching the index from the CDN.
    /// </summary>
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(30);

    private static string DefaultCacheDirectory()
        => Path.Combine(FuncHomeResolver.Resolve(), "template-engine", "search");
}
