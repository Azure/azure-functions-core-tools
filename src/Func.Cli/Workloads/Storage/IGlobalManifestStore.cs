// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Repository over the global workload manifest at
/// <see cref="IWorkloadPaths.GlobalManifestPath"/>. Hides the on-disk JSON
/// shape and the read-modify-write dance so callers can't accidentally race
/// on a shared mutable aggregate.
/// </summary>
/// <remarks>
/// The manifest models side-by-side installs (multiple versions of the
/// same package coexist), so reads, writes, and removes are scoped to a
/// (<c>packageId</c>, <c>version</c>) pair. Package-id matching is
/// case-insensitive (NuGet convention); version matching is ordinal.
/// </remarks>
internal interface IGlobalManifestStore
{
    /// <summary>
    /// Returns every installed (<c>packageId</c>, <c>version</c>) pair as a
    /// flat list. Returns an empty list when the manifest doesn't exist yet.
    /// Order is unspecified.
    /// </summary>
    public Task<IReadOnlyList<InstalledWorkload>> GetWorkloadsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts <paramref name="entry"/> under
    /// (<paramref name="packageId"/>, <paramref name="version"/>), or
    /// replaces an existing entry with the same pair. Other versions of the
    /// same package are left untouched (side-by-side).
    /// </summary>
    public Task SaveWorkloadAsync(
        string packageId,
        string version,
        GlobalManifestEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the entry for (<paramref name="packageId"/>,
    /// <paramref name="version"/>). Returns <c>true</c> when an entry was
    /// removed, <c>false</c> when no such entry existed. Other versions of
    /// the same package are left untouched.
    /// </summary>
    public Task<bool> RemoveWorkloadAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default);
}
