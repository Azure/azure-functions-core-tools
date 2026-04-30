// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Filesystem layout for installed workloads. Computed from
/// <see cref="WorkloadPathsOptions.Home"/>.
/// </summary>
internal interface IWorkloadPaths
{
    /// <summary>
    /// Root directory the func CLI persists workloads under.
    /// </summary>
    public string Home { get; }

    /// <summary>
    /// Directory containing all installed workload packages.
    /// </summary>
    public string WorkloadsRoot { get; }

    /// <summary>
    /// Absolute path to the global workload manifest file.
    /// </summary>
    public string GlobalManifestPath { get; }

    /// <summary>
    /// Per-package install directory inside <see cref="WorkloadsRoot"/>, namespaced by version.
    /// </summary>
    public string GetInstallDirectory(string packageId, string version);
}
