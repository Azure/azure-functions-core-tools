// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Loading;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// CLI-side view of an installed workload package.
/// </summary>
/// <param name="Kind">Installed package shape.</param>
/// <param name="PackageId">NuGet package id from the registry entry.</param>
/// <param name="PackageVersion">Installed package version from the registry entry.</param>
/// <param name="Aliases">User-facing tokens accepted by workload commands.</param>
/// <param name="InstallDirectory">Package install directory under the workload home.</param>
/// <param name="ContentRoot">Directory containing package payload files.</param>
/// <param name="DisplayName">Human-readable name for <c>func workload list</c>.</param>
/// <param name="Description">One-line workload description.</param>
internal abstract record WorkloadInfo(
    WorkloadKind Kind,
    string PackageId,
    string PackageVersion,
    IReadOnlyList<string> Aliases,
    string InstallDirectory,
    string ContentRoot,
    string DisplayName,
    string Description);

/// <summary>
/// CLI-side view of a loaded runtime workload.
/// </summary>
/// <param name="Instance">The loaded workload's runtime instance.</param>
/// <param name="PackageId">NuGet package id from the registry entry.</param>
/// <param name="PackageVersion">Installed package version from the registry entry.</param>
/// <param name="Aliases">User-facing tokens accepted by workload commands.</param>
/// <param name="InstallDirectory">Package install directory under the workload home.</param>
/// <param name="ContentRoot">Directory containing package payload files.</param>
/// <param name="DisplayName">Human-readable name for <c>func workload list</c>.</param>
/// <param name="Description">One-line workload description.</param>
/// <param name="LoadContext">The assembly load context used to load the workload.</param>
/// <remarks>
/// The <see cref="LoadContext"/> must be kept in memory as long as the workload is in use.
/// </remarks>
internal sealed record RuntimeWorkloadInfo(
    Workload Instance,
    string PackageId,
    string PackageVersion,
    IReadOnlyList<string> Aliases,
    string InstallDirectory,
    string ContentRoot,
    string DisplayName,
    string Description,
    WorkloadLoadContext LoadContext)
    : WorkloadInfo(WorkloadKind.Workload, PackageId, PackageVersion, Aliases, InstallDirectory, ContentRoot, DisplayName, Description);

/// <summary>
/// CLI-side view of an installed content workload.
/// </summary>
/// <param name="PackageId">NuGet package id from the registry entry.</param>
/// <param name="PackageVersion">Installed package version from the registry entry.</param>
/// <param name="Aliases">User-facing tokens accepted by workload commands.</param>
/// <param name="InstallDirectory">Package install directory under the workload home.</param>
/// <param name="ContentRoot">Directory containing package payload files.</param>
/// <param name="DisplayName">Human-readable name for <c>func workload list</c>.</param>
/// <param name="Description">One-line workload description.</param>
internal sealed record ContentWorkloadInfo(
    string PackageId,
    string PackageVersion,
    IReadOnlyList<string> Aliases,
    string InstallDirectory,
    string ContentRoot,
    string DisplayName,
    string Description)
    : WorkloadInfo(WorkloadKind.Content, PackageId, PackageVersion, Aliases, InstallDirectory, ContentRoot, DisplayName, Description);
