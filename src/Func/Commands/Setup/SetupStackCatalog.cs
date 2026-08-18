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
/// <param name="StackPackageIds">Primary stack name to the package that declares it.</param>
/// <param name="TemplatesPackageIds">Primary stack name to its templates content package.</param>
/// <param name="AmbiguousAliases">
/// Aliases claimed by more than one package. These are excluded from the maps
/// above rather than resolved arbitrarily, so a mis-tagged or hostile feed
/// can't decide which package <c>func setup</c> installs.
/// </param>
/// <param name="SecondaryAliases">
/// Alternate alias to the primary name for the same package. A package may
/// publish several interchangeable aliases, but only the first names the stack:
/// worker ids, templates, and profile runtimes are all keyed off that one, so
/// every other spelling has to fold into it before anything is planned.
/// </param>
internal sealed record SetupStackSnapshot(
    IReadOnlyDictionary<string, string> StackPackageIds,
    IReadOnlyDictionary<string, string> TemplatesPackageIds,
    IReadOnlySet<string>? AmbiguousAliases = null,
    IReadOnlyDictionary<string, string>? SecondaryAliases = null)
{
    public IReadOnlyList<string> StackNames => [.. StackPackageIds.Keys];

    public bool SupportsStack(string stack) => StackPackageId(stack) is not null;

    public bool SupportsTemplates(string stack) => TemplatesPackageId(stack) is not null;

    /// <summary>
    /// Folds an alternate alias onto the name the rest of setup plans against.
    /// Returns the input trimmed when it is already primary or unknown.
    /// </summary>
    public string CanonicalStackName(string stack)
    {
        if (string.IsNullOrWhiteSpace(stack))
        {
            return stack;
        }

        string trimmed = stack.Trim();
        return SecondaryAliases is { } aliases && aliases.TryGetValue(trimmed, out string? primary) ? primary : trimmed;
    }

    public string? StackPackageId(string stack)
        => !string.IsNullOrWhiteSpace(stack) && StackPackageIds.TryGetValue(CanonicalStackName(stack), out string? id) ? id : null;

    public string? TemplatesPackageId(string stack)
        => !string.IsNullOrWhiteSpace(stack) && TemplatesPackageIds.TryGetValue(CanonicalStackName(stack), out string? id) ? id : null;

    public bool IsAmbiguous(string alias)
        => AmbiguousAliases is { } ambiguous
            && !string.IsNullOrWhiteSpace(alias)
            && ambiguous.Contains(alias.Trim());
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

        // Fallback snapshots are cached too, so one unreachable feed doesn't make
        // every subsequent profile scope re-pay the timeout.
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

                // Stop on an empty page, not a short one: the catalog filters
                // hits client-side, so a full raw page can arrive here with only
                // a handful of workloads and more still to come. A feed that
                // ignores packageType can still filter a whole page to nothing
                // and cut discovery short; terminating on server page metadata
                // needs a wider catalog change, tracked by #5562. The built-in
                // fallback covers that case in the meantime.
                if (page.Count == 0)
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

        Dictionary<string, string> claims = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> templates = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> ambiguous = new(StringComparer.OrdinalIgnoreCase);

        // Every alias competes for ownership, so conflicts have to be settled
        // before deciding which name is primary. A rogue package claiming
        // another's alternate spelling is the same attack as claiming its main one.
        foreach (CatalogSearchResult result in results)
        {
            bool isStack = string.Equals(result.Kind, StackKind, StringComparison.OrdinalIgnoreCase);

            foreach (string alias in result.Aliases)
            {
                if (isStack)
                {
                    Claim(claims, ambiguous, alias, result.PackageId);
                }
                else if (alias.EndsWith(TemplatesAliasSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    string stackName = alias[..^TemplatesAliasSuffix.Length];
                    if (stackName.Length > 0)
                    {
                        Claim(templates, ambiguous, stackName, result.PackageId);
                    }
                }
            }
        }

        Dictionary<string, string> stacks = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> secondary = new(StringComparer.OrdinalIgnoreCase);

        foreach (CatalogSearchResult result in results)
        {
            if (!string.Equals(result.Kind, StackKind, StringComparison.OrdinalIgnoreCase)
                || result.Aliases.Count == 0)
            {
                continue;
            }

            string primary = result.Aliases[0];
            if (ambiguous.Contains(primary) || !OwnedBy(claims, primary, result.PackageId))
            {
                continue;
            }

            stacks[primary] = result.PackageId;

            foreach (string alias in result.Aliases.Skip(1))
            {
                if (!ambiguous.Contains(alias) && OwnedBy(claims, alias, result.PackageId))
                {
                    secondary[alias] = primary;
                }
            }
        }

        foreach (string alias in ambiguous)
        {
            templates.Remove(alias);
        }

        if (stacks.Count > 0)
        {
            return new SetupStackSnapshot(stacks, templates, ambiguous, secondary);
        }

        // An empty result usually means the query failed silently rather than
        // "no stacks exist", so prefer the built-in list over offering nothing.
        // Conflicting claims still travel with it, otherwise a feed where every
        // alias collides would empty the map and get the built-in ids waved
        // through as if nothing were wrong.
        return ambiguous.Count == 0
            ? SetupDependency.BuiltInStackSnapshot
            : SetupDependency.BuiltInStackSnapshot with { AmbiguousAliases = ambiguous };
    }

    /// <summary>
    /// Records an alias claim, flagging it as ambiguous when a second package
    /// claims the same alias with a different id. Mirrors the rejection
    /// <see cref="WorkloadPackageSource"/> applies to alias installs, rather
    /// than letting catalog ordering decide.
    /// </summary>
    /// <returns><c>true</c> when the alias was recorded for the first time.</returns>
    private static bool Claim(
        Dictionary<string, string> map,
        HashSet<string> ambiguous,
        string alias,
        string packageId)
    {
        if (map.TryGetValue(alias, out string? existing))
        {
            if (!string.Equals(existing, packageId, StringComparison.OrdinalIgnoreCase))
            {
                ambiguous.Add(alias);
            }

            return false;
        }

        map[alias] = packageId;
        return true;
    }

    private static bool OwnedBy(Dictionary<string, string> claims, string alias, string packageId)
        => claims.TryGetValue(alias, out string? owner)
            && string.Equals(owner, packageId, StringComparison.OrdinalIgnoreCase);
}
