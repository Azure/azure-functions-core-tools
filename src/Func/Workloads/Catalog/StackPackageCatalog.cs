// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Catalog;

internal sealed record StackPackage(StackDescriptor Stack, string PackageId, NuGetVersion Version, string Source, bool Installed);

internal sealed record InstalledStackInventory(IReadOnlyList<StackPackage> Stacks, IReadOnlyList<WorkloadEntry> CompatibleEntries)
{
    public bool HasPackage(string packageId, bool includePrerelease)
        => CompatibleEntries.Any(e => (string.Equals(e.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.LogicalPackage?.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            && NuGetVersion.TryParse(e.PackageVersion, out NuGetVersion? version) && (includePrerelease || !version.IsPrerelease));
}

/// <summary>
/// Associates stack declarations with the actual package that supplied them.
/// Reading the installed inventory never contacts a package source.
/// </summary>
internal sealed class StackPackageCatalog(IWorkloadStore store, IWorkloadCatalog catalog, IWorkloadRuntimeIdentifierProvider runtimeIdentifierProvider)
{
    private readonly IWorkloadStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IWorkloadCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly IWorkloadRuntimeIdentifierProvider _runtimeIdentifierProvider = runtimeIdentifierProvider ?? throw new ArgumentNullException(nameof(runtimeIdentifierProvider));

    public async Task<InstalledStackInventory> GetInstalledAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkloadEntry> entries = await _store.GetWorkloadsAsync(cancellationToken);
        string rid = _runtimeIdentifierProvider.Current;
        WorkloadEntry[] compatible = [.. entries.Where(e => WorkloadActivation.IsCompatible(e.RuntimeIdentifier, rid))];
        IReadOnlyList<WorkloadEntry> live = WorkloadActivation.SelectLiveRuntimeEntries(compatible);
        StackPackage[] stacks = [.. live.Where(e => e.Stack is not null)
            .Select(e => new StackPackage(e.Stack!, e.LogicalPackage?.PackageId ?? e.PackageId,
                WorkloadActivation.ParseVersionOrZero(e.LogicalPackage?.PackageVersion ?? e.PackageVersion), e.LogicalPackage?.Source ?? e.Source, Installed: true))
            .OrderByDescending(p => p.Version)
            .DistinctBy(p => p.PackageId, StringComparer.OrdinalIgnoreCase)];
        return new InstalledStackInventory(stacks, compatible);
    }

    public async Task<IReadOnlyList<StackPackage>> DiscoverAsync(
        IReadOnlyList<StackPackage> installed, string? source, bool includePrerelease, CancellationToken cancellationToken)
    {
        IReadOnlyList<StackPackage> available = await ReadAvailableAsync(null, source, includePrerelease, cancellationToken);

        // Prefer a local declaration to a renamed feed label. Otherwise use the latest
        // version that actually declares this ID, keeping competing owners separate.
        return [.. installed.Concat(available)
            .GroupBy(p => p.PackageId, StringComparer.OrdinalIgnoreCase)
            .SelectMany(owner => owner.DistinctBy(p => p.Stack.Identifier, StringComparer.OrdinalIgnoreCase))];
    }

    public async Task<StackPackage?> FindLatestAsync(StackPackage owner, string? workerRuntime, string? source, bool includePrerelease, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        IReadOnlyList<StackPackage> available = await ReadAvailableAsync(owner.PackageId, source, includePrerelease, cancellationToken);
        return available.FirstOrDefault(p => string.Equals(p.PackageId, owner.PackageId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.Stack.Identifier, owner.Stack.Identifier, StringComparison.OrdinalIgnoreCase)
            && (workerRuntime is null || string.Equals(p.Stack.WorkerRuntime, workerRuntime, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<IReadOnlyList<StackPackage>> ReadAvailableAsync(string? packageId, string? source, bool includePrerelease, CancellationToken cancellationToken)
    {
        IReadOnlyList<CatalogSearchResult> rows = await _catalog.SearchVersionsAsync(
            new CatalogSearchQuery { Filter = packageId, Source = source, IncludePrerelease = includePrerelease }, cancellationToken);
        string rid = _runtimeIdentifierProvider.Current;
        return [.. rows.Where(r => r.Stack is not null && WorkloadActivation.IsCompatible(r.Rid, rid) && (includePrerelease || !r.LatestVersion.IsPrerelease))
            .OrderByDescending(r => r.LatestVersion)
            .Select(r => new StackPackage(r.Stack!, r.PackageId, r.LatestVersion, r.Source.Source, Installed: false))];
    }
}
