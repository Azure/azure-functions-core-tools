// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel.DataAnnotations;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Computed filesystem layout for installed workloads. <see cref="Home"/> is
/// populated by <see cref="WorkloadPathsOptionsSetup"/> from the
/// <see cref="Constants.WorkloadsHomeEnvironmentVariable"/> env var (when
/// explicitly set) or the default user-profile path. Other configuration
/// sources (json files, local.settings, in-memory) are intentionally not
/// honored so the workload root stays predictable.
/// </summary>
/// <remarks>
/// Only <see cref="Home"/> is settable. Everything else is computed from it
/// and exposed through <see cref="IWorkloadPaths"/>.
/// </remarks>
internal sealed class WorkloadPathsOptions : IWorkloadPaths
{
    /// <summary>
    /// Filename of the global workload registry within <see cref="Home"/>.
    /// </summary>
    public const string WorkloadRegistryFileName = "workloads.json";

    /// <summary>
    /// Root directory the func CLI persists workloads under. Populated by
    /// <see cref="WorkloadPathsOptionsSetup"/>; defaults to <see cref="string.Empty"/>
    /// so validation fails fast if the setup is not wired up.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string Home { get; set; } = string.Empty;

    /// <inheritdoc />
    public string WorkloadsRoot => Path.Combine(Home, "workloads");

    /// <inheritdoc />
    public string WorkloadRegistryPath => Path.Combine(Home, WorkloadRegistryFileName);

    /// <inheritdoc />
    public string GetInstallDirectory(string packageId, string version)
        => Path.Combine(WorkloadsRoot, packageId, version);
}
