// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Search;

/// <summary>
/// In-memory projection of the func template search index — the
/// <c>NuGetTemplateSearchInfoVer2.json</c> search-cache format produced by the
/// discovery tool (<c>tools/TemplateDiscovery</c>). Only the fields the CLI's
/// search surface needs are modelled; the wire format carries more.
/// </summary>
/// <param name="Version">Search-cache schema version (<c>"2.0"</c>).</param>
/// <param name="Packages">Indexed template packages.</param>
internal sealed record FuncSearchIndex(string Version, IReadOnlyList<FuncSearchPackage> Packages);

/// <summary>
/// A single template package row in the search index.
/// </summary>
/// <param name="Name">NuGet package id.</param>
/// <param name="Version">Latest indexed version, when recorded.</param>
/// <param name="Owners">Package owners as recorded on the feed.</param>
/// <param name="Description">Package description, when recorded.</param>
/// <param name="Templates">Templates the package contributes.</param>
internal sealed record FuncSearchPackage(
    string Name,
    string? Version,
    IReadOnlyList<string> Owners,
    string? Description,
    IReadOnlyList<FuncSearchTemplate> Templates);

/// <summary>
/// A single template entry within an indexed package.
/// </summary>
/// <param name="Identity">Stable template identity.</param>
/// <param name="Name">Human-readable template name.</param>
/// <param name="ShortNameList">Short names usable with <c>func new --template</c>.</param>
/// <param name="Author">Template author, when recorded.</param>
/// <param name="Description">Template description, when recorded.</param>
/// <param name="Classifications">Free-form classifications (e.g. "Azure Function", "Http").</param>
/// <param name="Tags">Tag collection, including <c>azfunc-stack</c> / <c>language</c> / <c>azfunc-trigger</c>.</param>
internal sealed record FuncSearchTemplate(
    string Identity,
    string Name,
    IReadOnlyList<string> ShortNameList,
    string? Author,
    string? Description,
    IReadOnlyList<string> Classifications,
    IReadOnlyDictionary<string, string> Tags);
