// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//
// Vendored and adapted from the .NET Templating engine:
//   repo:   https://github.com/dotnet/templating
//   source: src/Microsoft.TemplateSearch.Common/TemplateSearchCache/*.Json.cs
//   version: 10.0.302
// See README.md in this directory for the full provenance note and list of adaptations.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.TemplateDiscovery;

/// <summary>
/// A candidate package discovered by an <see cref="IPackageProvider"/>, before it is scanned.
/// </summary>
internal sealed record CandidatePackage(
    string Name,
    string Version,
    long TotalDownloads,
    IReadOnlyList<string> Owners,
    bool Reserved,
    string? Description,
    string? IconUrl,
    string? LocalPath);

/// <summary>
/// A package that was inspected but produced no usable templates, recorded in <c>nonTemplatePacks.json</c>
/// so future incremental (<c>--diff</c>) runs can skip it.
/// </summary>
internal sealed record FilteredPackage(string Name, string Version, string Reason);

/// <summary>
/// The result of scanning a single candidate package with the template engine.
/// </summary>
internal sealed record ScannedPackage(CandidatePackage Package, IReadOnlyList<ITemplateInfo> Templates);
