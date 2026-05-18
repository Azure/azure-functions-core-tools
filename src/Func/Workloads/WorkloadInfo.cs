// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// CLI-side view of a loaded workload. Hides the fact that a workload's
/// metadata comes from two sources: identity (<see cref="PackageId"/>,
/// <see cref="PackageVersion"/>, <see cref="Aliases"/>) is sourced from the
/// global registry (recorded at install time from the package's nuspec);
/// presentation (<see cref="DisplayName"/>, <see cref="Description"/>) is
/// sourced from the loaded <see cref="Workload"/> instance. Consumers see
/// one cohesive record. Internal: workload authors implement
/// <see cref="Workload"/>; they don't see this type.
/// </summary>
/// <param name="Instance">The loaded workload's runtime instance. Use this for behaviour (e.g. <see cref="Workload.Configure"/>); read presentation through <see cref="DisplayName"/> / <see cref="Description"/>.</param>
/// <param name="PackageId">NuGet package id from the registry entry (e.g. <c>"Azure.Functions.Cli.Workloads.Dotnet"</c>).</param>
/// <param name="PackageVersion">Installed package version from the registry entry.</param>
/// <param name="Aliases">User-facing tokens accepted by <c>-s</c> (e.g. <c>["dotnet", "dotnet-isolated"]</c>).</param>
/// <param name="DisplayName">Human-readable name for <c>func workload list</c>; sourced from <see cref="Workload.DisplayName"/>.</param>
/// <param name="Description">One-line workload description; sourced from <see cref="Workload.Description"/>.</param>
internal sealed record WorkloadInfo(
    Workload Instance,
    string PackageId,
    string PackageVersion,
    IReadOnlyList<string> Aliases,
    string DisplayName,
    string Description);
