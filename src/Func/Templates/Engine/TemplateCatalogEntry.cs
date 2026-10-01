// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Func-owned description of an installed template.
/// </summary>
/// <param name="Identity">Unique template identity.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">Description, or <c>null</c> when the template gives none.</param>
/// <param name="Type">Template type the template declares in <c>tags.type</c>.</param>
/// <param name="ShortNames">Short names that select the template.</param>
/// <param name="GroupIdentity">Identity the template shares with its variants, or <c>null</c> when it's ungrouped.</param>
/// <param name="Language">Language the template declares in <c>tags.language</c>, or <c>null</c> when it declares none.</param>
/// <param name="Precedence">Precedence of the template among its variants.</param>
/// <param name="Package">Package that provides the template, or <c>null</c> when its details aren't available.</param>
/// <param name="IsHidden">
/// Whether <c>func.host.json</c> hides the template from ordinary listings, or <c>false</c> when
/// <paramref name="MetadataDiagnostic"/> is set.
/// </param>
/// <param name="Parameters">Parameters users can set, or empty when <paramref name="MetadataDiagnostic"/> is set.</param>
/// <param name="MetadataDiagnostic">Why the template's func host metadata can't be used, or <c>null</c> when it's valid.</param>
internal sealed record TemplateCatalogEntry(
    string Identity,
    string Name,
    string? Description,
    TemplateType Type,
    IReadOnlyList<string> ShortNames,
    string? GroupIdentity,
    string? Language,
    int Precedence,
    TemplatePackageInfo? Package,
    bool IsHidden,
    IReadOnlyList<TemplateParameterDefinition> Parameters,
    string? MetadataDiagnostic);

/// <summary>
/// Installed template package that provides a template.
/// </summary>
/// <param name="Identifier">Package identifier, such as a NuGet package ID or a folder path.</param>
/// <param name="Version">Package version, or <c>null</c> when the package has none, as with a folder.</param>
internal sealed record TemplatePackageInfo(string Identifier, string? Version);
