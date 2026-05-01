// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Install;

/// <summary>
/// Subset of a NuGet <c>.nuspec</c> the install pipeline needs. Other
/// metadata (authors, license, project URL, etc.) is intentionally ignored:
/// the install pipeline only persists what <c>func workload list</c> shows
/// and what the loader uses to activate a workload.
/// </summary>
/// <param name="PackageId">The <c>id</c> element. NuGet ids are case-insensitive.</param>
/// <param name="Version">The <c>version</c> element verbatim. Validation lives at the catalog layer.</param>
/// <param name="Title">The <c>title</c> element, or empty if absent. Falls back to <see cref="PackageId"/> in the manifest.</param>
/// <param name="Description">The <c>description</c> element, or empty if absent.</param>
/// <param name="Aliases">Whitespace-split <c>tags</c>, used as user-facing short names (e.g. <c>"dotnet"</c>).</param>
/// <param name="PackageTypes">Names from <c>packageTypes/packageType/@name</c>. Contains <c>"FuncCliWorkload"</c> for valid workload packages.</param>
internal sealed record NuspecMetadata(
    string PackageId,
    string Version,
    string Title,
    string Description,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> PackageTypes);
