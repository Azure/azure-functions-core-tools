// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Install;

/// <summary>
/// High-level stage of a workload install / update flow. Exposed so callers
/// (e.g. <c>WorkloadInstallCommand</c>) can translate phase transitions into
/// user-facing progress descriptions without duplicating phrasing across
/// install entry points.
/// </summary>
internal enum WorkloadInstallPhase
{
    /// <summary>Resolving the alias or package id against the catalog.</summary>
    Resolving,

    /// <summary>Downloading the resolved <c>.nupkg</c> from the source.</summary>
    Downloading,

    /// <summary>Extracting the package onto disk.</summary>
    Extracting,

    /// <summary>Persisting the install to the global registry.</summary>
    Registering,
}

/// <summary>
/// Progress payload reported by <see cref="IWorkloadInstaller"/> entry points.
/// </summary>
/// <param name="Phase">Coarse phase the installer just entered.</param>
/// <param name="Description">
/// Human-readable description of the in-flight work. Suitable for use as a
/// progress bar / spinner label. Already includes the package id where
/// relevant.
/// </param>
internal sealed record WorkloadInstallProgress(WorkloadInstallPhase Phase, string Description);
