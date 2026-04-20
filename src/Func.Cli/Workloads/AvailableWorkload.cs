// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Represents a workload that can be installed (from the built-in catalog or discovered via NuGet).
/// </summary>
/// <param name="Id">Short ID (e.g., "dotnet", "node").</param>
/// <param name="PackageId">Full NuGet package ID.</param>
/// <param name="Description">Human-friendly description.</param>
/// <param name="Languages">Comma-separated language names (e.g., "C#, F#").</param>
/// <param name="InstalledVersion">The installed version, or null if not installed.</param>
public record AvailableWorkload(
    string Id,
    string PackageId,
    string Description,
    string Languages,
    string? InstalledVersion = null)
{
    public bool IsInstalled => InstalledVersion is not null;
}
