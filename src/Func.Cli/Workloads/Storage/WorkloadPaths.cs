// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Default <see cref="IWorkloadPaths"/> implementation. Reads
/// <see cref="WorkloadPathsOptions.Home"/> once at construction time
/// (CLI config doesn't change mid-run) and computes everything else from it.
/// </summary>
internal sealed class WorkloadPaths(IOptions<WorkloadPathsOptions> options) : IWorkloadPaths
{
    /// <summary>
    /// Filename of the global workload manifest within <see cref="Home"/>.
    /// </summary>
    public const string GlobalManifestFileName = "workloads.json";

    private readonly string _home = options?.Value.Home
        ?? throw new ArgumentNullException(nameof(options));

    public string Home => _home;

    public string WorkloadsRoot => Path.Combine(_home, "workloads");

    public string GlobalManifestPath => Path.Combine(_home, GlobalManifestFileName);

    public string GetInstallDirectory(string packageId, string version)
        => Path.Combine(WorkloadsRoot, packageId, version);
}
