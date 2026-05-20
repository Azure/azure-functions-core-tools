// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Computed filesystem layout for installed workloads. The default
/// constructor resolves <see cref="Home"/> via
/// <see cref="WorkloadHomeResolver.Resolve"/>; the
/// <see cref="WorkloadPathsOptions(string)"/> overload lets tests inject
/// an explicit home without touching the real process environment.
/// </summary>
/// <remarks>
/// <see cref="Home"/> is assigned at construction (and normalised via
/// <see cref="Path.GetFullPath(string)"/>), so other configuration sources
/// (json files, local.settings, in-memory) are not honored. Computed
/// members surface through <see cref="IWorkloadPaths"/>.
/// </remarks>
internal sealed class WorkloadPathsOptions : IWorkloadPaths
{
    /// <summary>
    /// Filename of the global workload registry within <see cref="Home"/>.
    /// </summary>
    public const string WorkloadRegistryFileName = "workloads.json";

    /// <summary>
    /// Resolves <see cref="Home"/> from
    /// <see cref="Constants.WorkloadsHomeEnvironmentVariable"/>, falling
    /// back to the default user-profile path.
    /// </summary>
    public WorkloadPathsOptions()
        : this(WorkloadHomeResolver.Resolve())
    {
    }

    /// <summary>
    /// Test-only seam for supplying <see cref="Home"/> directly so unit and
    /// integration tests don't have to mutate process-global env vars.
    /// </summary>
    internal WorkloadPathsOptions(string home)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        Home = Path.GetFullPath(home);
    }

    /// <summary>
    /// Root directory the func CLI persists workloads under.
    /// </summary>
    public string Home { get; }

    /// <inheritdoc />
    public string WorkloadsRoot => Path.Combine(Home, "workloads");

    /// <inheritdoc />
    public string WorkloadRegistryPath => Path.Combine(Home, WorkloadRegistryFileName);

    /// <inheritdoc />
    public string GetInstallDirectory(string packageId, string version)
        => Path.Combine(WorkloadsRoot, packageId, version);
}
