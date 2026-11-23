// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Templates.Packages;

/// <summary>
/// Represents the stable machine-readable template package search surface.
/// </summary>
internal sealed record TemplatePackageSearchResult(
    string PackageId,
    string LatestVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Title,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Description);
