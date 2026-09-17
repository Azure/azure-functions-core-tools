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
    /// Falls back to the built-in list when discovery fails, retaining any
    /// alias conflicts already observed. Discovery is bounded and best-effort.
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
    /// <summary>
    /// Names safe to offer. Contested ones are withheld: planning refuses them,
    /// so listing them in the prompt only sets up a guaranteed failure. This
    /// matters on the fallback path, where the maps come from the built-in list
    /// but the conflicts came from the feed.
    /// </summary>
    public IReadOnlyList<string> StackNames
        => AmbiguousAliases is { Count: > 0 } ambiguous
            ? [.. StackPackageIds.Keys.Where(name => !ambiguous.Contains(name))]
            : [.. StackPackageIds.Keys];

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

    /// <summary>
    /// Resolves a stack to its package, or <c>null</c> when the name is unknown
    /// or contested. Contested names resolve to nothing here rather than
    /// relying on callers to ask <see cref="IsAmbiguous"/> first, since on the
    /// fallback path the built-in maps still hold an entry for them.
    /// </summary>
    public string? StackPackageId(string stack)
        => Resolve(StackPackageIds, stack);

    /// <inheritdoc cref="StackPackageId"/>
    public string? TemplatesPackageId(string stack)
        => Resolve(TemplatesPackageIds, stack);

    public bool IsAmbiguous(string alias)
        => AmbiguousAliases is { } ambiguous
            && !string.IsNullOrWhiteSpace(alias)
            && ambiguous.Contains(alias.Trim());

    private string? Resolve(IReadOnlyDictionary<string, string> map, string stack)
    {
        if (string.IsNullOrWhiteSpace(stack))
        {
            return null;
        }

        // Folding first means an alternate spelling of a contested stack is
        // refused along with it; a contested name is its own canonical, so the
        // direct case lands here too.
        string canonical = CanonicalStackName(stack);
        return !IsAmbiguous(canonical) && map.TryGetValue(canonical, out string? id) ? id : null;
    }
}

internal sealed class SetupStackCatalog(IWorkloadCatalog workloadCatalog) : ISetupStackCatalog
{
    // Mirrors the `kind:workload` PackageTag emitted by stack csprojs.
    private const string StackKind = "workload";
    private const string TemplatesAliasSuffix = "-templates";

    private const int PageSize = 100;

    // Bound the requests even when a feed never returns an empty page.
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
        bool useBuiltInFallback = false;
        try
        {
            // An empty filter is deliberate: the catalog pairs it with
            // packageType=FuncCliWorkload. Narrowing to a text term such as
            // `func-workload` excludes stacks that do not carry that tag.
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
                // a handful of workloads and more still to come. This still
                // reads a filtered count, so a feed that both ignores
                // packageType and reports packageTypes per hit could filter a
                // whole page away and cut discovery short. A partial stack map
                // does not trigger fallback; neither path recovers unseen claims.
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
            // Keep the offline fallback, but process the pages already received
            // so a later failure cannot erase known alias conflicts.
            useBuiltInFallback = true;
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
                // Ownership spans every kind, matching WorkloadPackageSource. A
                // stack reusing a worker or host alias has to collide here, or
                // setup would offer it and install it by exact id, never running
                // the check that would have refused the same alias by name.
                Claim(claims, ambiguous, alias, result.PackageId);

                if (!isStack && alias.EndsWith(TemplatesAliasSuffix, StringComparison.OrdinalIgnoreCase))
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

            // Record alternate spellings before deciding the primary's fate. If
            // the primary is contested they still have to fold onto it, or the
            // alternate escapes as an unknown runtime, misses the ambiguity
            // check that only knows the primary, and half-installs.
            foreach (string alias in result.Aliases.Skip(1))
            {
                if (!ambiguous.Contains(alias) && OwnedBy(claims, alias, result.PackageId))
                {
                    secondary[alias] = primary;
                }
            }

            if (ambiguous.Contains(primary) || !OwnedBy(claims, primary, result.PackageId))
            {
                continue;
            }

            stacks[primary] = result.PackageId;
        }

        foreach (string alias in ambiguous)
        {
            templates.Remove(alias);
        }

        if (!useBuiltInFallback && stacks.Count > 0)
        {
            return new SetupStackSnapshot(stacks, templates, ambiguous, secondary);
        }

        // Empty or failed discovery uses built-in ids. Observed conflicts and
        // their alternate spellings must still be refused on that fallback.
        // Uncontested aliases from partial results must not redirect built-in names.
        return ambiguous.Count == 0
            ? SetupDependency.BuiltInStackSnapshot
            : SetupDependency.BuiltInStackSnapshot with
            {
                AmbiguousAliases = ambiguous,
                SecondaryAliases = secondary
                    .Where(pair => ambiguous.Contains(pair.Value))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            };
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
