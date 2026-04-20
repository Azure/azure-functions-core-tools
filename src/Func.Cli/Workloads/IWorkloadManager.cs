// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Service interface for managing workloads — discovery, installation,
/// removal, and loading of installed workload assemblies.
/// </summary>
public interface IWorkloadManager
{
    /// <summary>
    /// Returns metadata for all installed workloads.
    /// </summary>
    public IReadOnlyList<WorkloadInfo> GetInstalledWorkloads();

    /// <summary>
    /// Installs a workload from a NuGet package.
    /// </summary>
    /// <param name="packageId">The NuGet package ID (e.g., "Azure.Functions.Cli.Workload.Dotnet").</param>
    /// <param name="version">Optional version constraint. If null, installs the latest.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The installed workload info.</returns>
    public Task<WorkloadInfo> InstallWorkloadAsync(string packageId, string? version = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uninstalls a workload by its ID.
    /// </summary>
    public Task UninstallWorkloadAsync(string workloadId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a workload to the latest (or specified) version.
    /// </summary>
    public Task<WorkloadInfo> UpdateWorkloadAsync(string workloadId, string? version = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads all installed workload assemblies and returns their IWorkload instances.
    /// Uses AssemblyLoadContext for isolation.
    /// </summary>
    public IReadOnlyList<IWorkload> LoadWorkloads();

    /// <summary>
    /// Waits for the background update check (started during LoadWorkloads)
    /// to complete and prints any available update notices.
    /// Call near the end of command execution so notices appear after main output.
    /// </summary>
    public Task PrintUpdateNoticesAsync();

    /// <summary>
    /// Returns all template providers from all loaded workloads.
    /// </summary>
    public IReadOnlyList<ITemplateProvider> GetAllTemplateProviders();

    /// <summary>
    /// Returns all project initializers from all loaded workloads.
    /// </summary>
    public IReadOnlyList<IProjectInitializer> GetAllProjectInitializers();

    /// <summary>
    /// Returns all pack providers from all loaded workloads.
    /// </summary>
    public IReadOnlyList<IPackProvider> GetAllPackProviders();

    /// <summary>
    /// Returns the available worker runtimes from installed workloads.
    /// </summary>
    public IReadOnlyList<string> GetAvailableRuntimes();

    /// <summary>
    /// Returns all known workloads (built-in catalog + NuGet discovery)
    /// with their install status.
    /// </summary>
    public Task<IReadOnlyList<AvailableWorkload>> GetAvailableWorkloadsAsync(
        CancellationToken cancellationToken = default);
}
