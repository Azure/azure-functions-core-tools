// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// CLI-side view of a workload entry from the global manifest
/// (<c>~/.azure-functions/workloads.json</c>). Hydrated by the install /
/// discovery layer; consumed by <c>func workload list</c> and other commands
/// that need to describe what's available. Internal — workload authors
/// implement <see cref="IWorkload"/>; they don't see this type.
/// </summary>
/// <param name="PackageId">NuGet package id (e.g. <c>"Azure.Functions.Cli.Workload.Dotnet"</c>).</param>
/// <param name="PackageVersion">Installed package version. Same field name as <see cref="IWorkload.PackageVersion"/>.</param>
/// <param name="DisplayName">Human-readable name.</param>
/// <param name="Description">One-line description.</param>
/// <param name="Aliases">User-facing tokens accepted by <c>-s</c> (e.g. <c>["dotnet", "dotnet-isolated"]</c>).</param>
internal sealed record WorkloadInfo(
    string PackageId,
    string PackageVersion,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Aliases);
