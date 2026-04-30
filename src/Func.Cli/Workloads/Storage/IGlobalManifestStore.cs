// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Repository over the global workload manifest at
/// <see cref="IWorkloadPaths.GlobalManifestPath"/>. Hides the on-disk JSON
/// shape and the read-modify-write dance so callers can't accidentally race
/// on a shared mutable aggregate.
/// </summary>
internal interface IGlobalManifestStore
{
    /// <summary>
    /// Returns every workload currently recorded in the manifest. Returns an
    /// empty list when the manifest doesn't exist yet.
    /// </summary>
    public Task<IReadOnlyList<GlobalManifestEntry>> GetWorkloadsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts <paramref name="entry"/>, or replaces an existing entry with
    /// the same <see cref="GlobalManifestEntry.PackageId"/> (case-insensitive).
    /// Used by both <c>workload install</c> (insert) and upgrade (replace).
    /// </summary>
    public Task SaveWorkloadAsync(GlobalManifestEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the entry whose <see cref="GlobalManifestEntry.PackageId"/>
    /// matches (case-insensitive). Returns <c>true</c> when an entry was
    /// removed, <c>false</c> when no matching entry existed.
    /// </summary>
    public Task<bool> RemoveWorkloadAsync(string packageId, CancellationToken cancellationToken = default);
}
