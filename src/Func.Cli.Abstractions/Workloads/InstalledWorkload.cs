// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Render-ready view of a workload entry from the global manifest
/// (<c>~/.azure-functions/workloads.json</c>). Populated by the install /
/// discovery layer; consumed by <c>func workload list</c> and other
/// commands that need to describe what's available.
/// </summary>
/// <param name="PackageId">NuGet package id (e.g. <c>"Azure.Functions.Cli.Workload.Dotnet"</c>).</param>
/// <param name="DisplayName">Human-readable name.</param>
/// <param name="Description">One-line description.</param>
/// <param name="Aliases">User-facing tokens accepted by <c>-s</c> (e.g. <c>["dotnet", "dotnet-isolated"]</c>).</param>
/// <param name="Version">Installed package version.</param>
/// <param name="Type">Workload category (stack / tool / extension).</param>
public sealed record InstalledWorkload(
    string PackageId,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Aliases,
    string Version,
    WorkloadType Type);
