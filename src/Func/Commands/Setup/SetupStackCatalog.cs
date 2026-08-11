// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using Azure.Functions.Cli.Workloads.Catalog;
using NuGet.Protocol.Core.Types;

namespace Azure.Functions.Cli.Commands.Setup;

/// <summary>
/// Discovers which stacks (and their templates content packages) are published,
/// so a new stack does not require a CLI release to become setup-able.
/// </summary>
internal interface ISetupStackCatalog
{
    /// <summary>
    /// Returns the published stack and templates packages keyed by stack name.
    /// Falls back to the built-in list when the catalog cannot be reached, so
    /// offline setup keeps working exactly as it did before.
    /// </summary>
    public Task<SetupStackSnapshot> GetStacksAsync(string? source, bool includePrerelease, CancellationToken cancellationToken);
}

/// <summary>
/// Stack names to package ids, resolved from <c>kind:</c> and <c>alias:</c> workload tags.
/// </summary>
internal sealed record SetupStackSnapshot(
    IReadOnlyDictionary<string, string> StackPackageIds,
    IReadOnlyDictionary<string, string> TemplatesPackageIds)
{
    public IReadOnlyList<string> StackNames => [.. StackPackageIds.Keys];

    public bool SupportsStack(string stack)
        => !string.IsNullOrWhiteSpace(stack) && StackPackageIds.ContainsKey(stack.Trim());

    public bool SupportsTemplates(string stack)
        => !string.IsNullOrWhiteSpace(stack) && TemplatesPackageIds.ContainsKey(stack.Trim());

    public string? StackPackageId(string stack)
        => StackPackageIds.TryGetValue(stack.Trim(), out string? id) ? id : null;

    public string? TemplatesPackageId(string stack)
        => TemplatesPackageIds.TryGetValue(stack.Trim(), out string? id) ? id : null;
}

internal sealed class SetupStackCatalog(IWorkloadCatalog workloadCatalog) : ISetupStackCatalog
{
    // Mirrors the `kind:workload` PackageTag that stack csprojs emit; every other
    // workload shape (host, bundles, workers, templates) packs as `kind:content`.
    private const string StackKind = "workload";
    private const string TemplatesAliasSuffix = "-templates";

    private const int PageSize = 100;

    // Upper bound on the pages walked, so a feed that always returns a full
    // page can't spin forever. Well above the ~21 workloads published today.
    private const int MaxDiscoveredPackages = 1000;

    private readonly IWorkloadCatalog _workloadCatalog = workloadCatalog ?? throw new ArgumentNullException(nameof(workloadCatalog));
    private readonly ConcurrentDictionary<string, SetupStackSnapshot> _cache = new(StringComparer.Ordinal);

    public async Task<SetupStackSnapshot> GetStacksAsync(string? source, bool includePrerelease, CancellationToken cancellationToken)
    {
        string cacheKey = $"{source}|{includePrerelease}";
        if (_cache.TryGetValue(cacheKey, out SetupStackSnapshot? cached))
        {
            return cached;
        }

        SetupStackSnapshot snapshot = await DiscoverAsync(source, includePrerelease, cancellationToken);
        _cache[cacheKey] = snapshot;
        return snapshot;
    }

    private async Task<SetupStackSnapshot> DiscoverAsync(string? source, bool includePrerelease, CancellationToken cancellationToken)
    {
        List<CatalogSearchResult> results = [];
        try
        {
            // An empty filter is deliberate: the catalog pairs it with
            // packageType=FuncCliWorkload, which nuget.org honours, so this
            // returns the full workload set (measured 2026-08-11: 21 of 21 hits
            // were workloads). Narrowing to a term such as the shared
            // `func-workload` tag returns fewer rows and drops stacks.
            for (int skip = 0; skip < MaxDiscoveredPackages; skip += PageSize)
            {
                IReadOnlyList<CatalogSearchResult> page = await _workloadCatalog.SearchAsync(
                    new CatalogSearchQuery
                    {
                        IncludePrerelease = includePrerelease,
                        Skip = skip,
                        Take = PageSize,
                        Source = source,
                    },
                    cancellationToken);

                results.AddRange(page);
                if (page.Count < PageSize)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is ArgumentException
            or InvalidOperationException
            or IOException
            or HttpRequestException
            or FatalProtocolException)
        {
            // Offline, unreachable feed, or a malformed response. Setup still
            // needs to work against already-installed workloads, so use the
            // built-in list. Anything else is a bug and should surface.
            return SetupDependency.BuiltInStackSnapshot;
        }

        Dictionary<string, string> stacks = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> templates = new(StringComparer.OrdinalIgnoreCase);

        foreach (CatalogSearchResult result in results)
        {
            foreach (string alias in result.Aliases)
            {
                if (string.Equals(result.Kind, StackKind, StringComparison.OrdinalIgnoreCase))
                {
                    stacks.TryAdd(alias, result.PackageId);
                }
                else if (alias.EndsWith(TemplatesAliasSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    string stackName = alias[..^TemplatesAliasSuffix.Length];
                    if (stackName.Length > 0)
                    {
                        templates.TryAdd(stackName, result.PackageId);
                    }
                }
            }
        }

        // An empty result usually means the query failed silently rather than
        // "no stacks exist", so prefer the built-in list over offering nothing.
        return stacks.Count == 0
            ? SetupDependency.BuiltInStackSnapshot
            : new SetupStackSnapshot(stacks, templates);
    }
}
