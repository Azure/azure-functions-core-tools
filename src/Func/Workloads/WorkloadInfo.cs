// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// CLI-side view of a loaded workload. Pairs the runtime <see cref="Workload"/>
/// instance with the package identity recorded in the global registry. Internal
/// — workload authors implement <see cref="Workload"/>; they don't see this
/// type.
/// </summary>
/// <param name="Instance">The loaded workload's runtime instance.</param>
/// <param name="PackageId">NuGet package id from the registry entry (e.g. <c>"Azure.Functions.Cli.Workload.Dotnet"</c>).</param>
/// <param name="PackageVersion">Installed package version from the registry entry.</param>
/// <param name="Aliases">User-facing tokens accepted by <c>-s</c> (e.g. <c>["dotnet", "dotnet-isolated"]</c>).</param>
internal sealed record WorkloadInfo(
    Workload Instance,
    string PackageId,
    string PackageVersion,
    IReadOnlyList<string> Aliases);
