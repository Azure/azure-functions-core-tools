// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Repository over the global workload registry at
/// <see cref="IWorkloadPaths.WorkloadRegistryPath"/>. Hides the on-disk JSON
/// shape and the read-modify-write dance so callers can't accidentally race
/// on a shared mutable aggregate.
/// </summary>
/// <remarks>
/// The registry models side-by-side installs (multiple versions of the same
/// package coexist), so reads, writes, and removes are scoped to a
/// (<c>packageId</c>, <c>version</c>) pair. Package-id matching is
/// case-insensitive (NuGet convention); version matching is ordinal.
/// </remarks>
internal interface IWorkloadStore
{
    /// <summary>
    /// Returns every installed workload as a flat list. Returns an empty list
    /// when the registry doesn't exist yet. Order is unspecified.
    /// </summary>
    public Task<IReadOnlyList<WorkloadEntry>> GetWorkloadsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts <paramref name="entry"/>, or replaces an existing entry with
    /// the same (<see cref="WorkloadEntry.PackageId"/>,
    /// <see cref="WorkloadEntry.PackageVersion"/>) pair. Other versions of
    /// the same package are left untouched (side-by-side).
    /// </summary>
    public Task SaveWorkloadAsync(WorkloadEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the entry for (<paramref name="packageId"/>,
    /// <paramref name="version"/>). Returns <c>true</c> when an entry was
    /// removed, <c>false</c> when no such entry existed. Other versions of
    /// the same package are left untouched.
    /// </summary>
    public Task<bool> RemoveWorkloadAsync(string packageId, string version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically removes the entry for (<paramref name="oldPackageId"/>,
    /// <paramref name="oldVersion"/>) and inserts <paramref name="newEntry"/>
    /// in a single registry write. Used by <c>func workload update</c>
    /// (spec §6.4 step 4) to avoid leaving the registry in a half-swapped
    /// state if the process dies mid-write.
    /// </summary>
    public Task ReplaceWorkloadAsync(
        string oldPackageId,
        string oldVersion,
        WorkloadEntry newEntry,
        CancellationToken cancellationToken = default);
}
