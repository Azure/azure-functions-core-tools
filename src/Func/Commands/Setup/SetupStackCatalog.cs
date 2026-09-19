// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Install;
using NuGet.Protocol.Core.Types;

namespace Azure.Functions.Cli.Commands.Setup;

/// <summary>
/// Discovers which stacks (and their templates content packages) are published,
/// so a new stack does not require a CLI release to become setup-able.
/// </summary>
internal interface ISetupStackCatalog
{
    /// <summary>
    /// Returns published stack and templates packages keyed by stack name, using
    /// built-in fallback only for transport failures or discovery with no stacks
    /// while retaining observed alias conflicts and unsupported roles. Malformed
    /// responses, invalid sources or metadata, and incomplete bounded discovery abort setup.
    /// </summary>
    public Task<SetupStackSnapshot> GetStacksAsync(string? source, bool includePrerelease, CancellationToken cancellationToken);
}

/// <summary>
/// Stack names to package ids, resolved from <c>kind:</c> and <c>alias:</c> workload tags.
/// </summary>
/// <param name="StackPackageIds">Primary stack name to the package that declares it.</param>
/// <param name="TemplatesPackageIds">Primary stack name to its templates content package.</param>
/// <param name="AmbiguousAliases">
/// Aliases claimed by more than one package. Accessors refuse these names even
/// when fallback maps contain them, rather than resolving ownership arbitrarily.
/// </param>
/// <param name="SecondaryAliases">
/// Alternate alias to the primary name for the same package. A package may
/// publish several interchangeable aliases, but one canonical alias names the stack:
/// worker ids, templates, and profile runtimes are all keyed off that one, so
/// every other spelling has to fold into it before anything is planned.
/// </param>
/// <param name="AliasConflicts">Observed alias claims explaining each contested name.</param>
/// <param name="UnsupportedAliases">Observed aliases for RID pointers or RID-specific stack and templates packages.</param>
/// <param name="IsFallback">Whether package maps are built-in defaults rather than a completed discovery result.</param>
internal sealed record SetupStackSnapshot(
    IReadOnlyDictionary<string, string> StackPackageIds,
    IReadOnlyDictionary<string, string> TemplatesPackageIds,
    IReadOnlySet<string>? AmbiguousAliases = null,
    IReadOnlyDictionary<string, string>? SecondaryAliases = null,
    IReadOnlyDictionary<string, IReadOnlyList<SetupAliasConflict>>? AliasConflicts = null,
    IReadOnlySet<string>? UnsupportedAliases = null,
    bool IsFallback = false)
{
    public IReadOnlyList<SetupAliasConflict> ConflictsFor(string name)
        => AliasConflicts is { } conflicts && conflicts.TryGetValue(CanonicalStackName(name), out IReadOnlyList<SetupAliasConflict>? found)
            ? found
            : [];

    /// <summary>
    /// Names whose packages can be resolved, excluding contested or unsupported
    /// primaries and their alternate spellings even on fallback.
    /// </summary>
    public IReadOnlyList<string> StackNames
        => [.. StackPackageIds.Keys.Where(SupportsStack)];

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
    /// Resolves a stack to its package, refusing unknown, contested or unsupported
    /// names even when fallback maps still contain them.
    /// </summary>
    public string? StackPackageId(string stack)
        => Resolve(StackPackageIds, stack);

    /// <inheritdoc cref="StackPackageId"/>
    public string? TemplatesPackageId(string stack)
    {
        string templatesAlias = CanonicalStackName(stack) + "-templates";
        return !IsAmbiguous(templatesAlias) && !IsUnsupported(templatesAlias) ? Resolve(TemplatesPackageIds, stack) : null;
    }

    public bool IsAmbiguous(string alias)
        => AmbiguousAliases is { } ambiguous
            && !string.IsNullOrWhiteSpace(alias)
            && ambiguous.Contains(alias.Trim());

    public bool IsUnsupported(string alias)
        => UnsupportedAliases is { } unsupported
            && !string.IsNullOrWhiteSpace(alias)
            && unsupported.Contains(alias.Trim());

    private string? Resolve(IReadOnlyDictionary<string, string> map, string stack)
    {
        if (string.IsNullOrWhiteSpace(stack) || IsAmbiguous(stack) || IsUnsupported(stack))
        {
            return null;
        }

        // An alternate spelling must not bypass its primary's restrictions.
        string canonical = CanonicalStackName(stack);
        return !IsAmbiguous(canonical) && !IsUnsupported(canonical) && map.TryGetValue(canonical, out string? id) ? id : null;
    }
}

internal sealed record SetupAliasConflict(string Alias, IReadOnlyList<string> PackageIds);

internal sealed class SetupStackCatalog(IWorkloadCatalog workloadCatalog) : ISetupStackCatalog
{
    // Mirrors the `kind:workload` PackageTag emitted by stack csprojs.
    private const string StackKind = "workload";
    private const string TemplatesAliasSuffix = "-templates";

    private const int PageSize = 100;

    // Bound both raw rows and requests, including feeds that clamp every page.
    private const int MaxDiscoveredPackages = 1000;
    private const int MaxDiscoveryRequests = 32;

    private readonly IWorkloadCatalog _workloadCatalog = workloadCatalog ?? throw new ArgumentNullException(nameof(workloadCatalog));
    private readonly ConcurrentDictionary<string, SetupStackSnapshot> _cache = new(StringComparer.Ordinal);

    public async Task<SetupStackSnapshot> GetStacksAsync(string? source, bool includePrerelease, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string cacheKey = $"{source}|{includePrerelease}";
        if (_cache.TryGetValue(cacheKey, out SetupStackSnapshot? cached))
        {
            return cached;
        }

        // Fallback snapshots are cached too, so one unreachable feed doesn't make
        // every subsequent profile scope re-pay the timeout.
        SetupStackSnapshot snapshot = await DiscoverAsync(source, includePrerelease, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _cache[cacheKey] = snapshot;
        return snapshot;
    }

    private async Task<SetupStackSnapshot> DiscoverAsync(string? source, bool includePrerelease, CancellationToken cancellationToken)
    {
        List<CatalogSearchResult> results = [];
        bool useBuiltInFallback = false;
        bool reachedEnd = false;
        try
        {
            // An empty filter is deliberate: the catalog pairs it with
            // packageType=FuncCliWorkload. Narrowing to a text term such as
            // `func-workload` excludes stacks that do not carry that tag.
            int skip = 0;
            long? expectedTotal = null;
            for (int request = 0; skip < MaxDiscoveredPackages && request < MaxDiscoveryRequests; request++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int take = Math.Min(PageSize, MaxDiscoveredPackages - skip);
                CatalogSearchPage page = await _workloadCatalog.SearchPageAsync(
                    new CatalogSearchQuery
                    {
                        IncludePrerelease = includePrerelease,
                        Skip = skip,
                        Take = take,
                        Source = source,
                    },
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                if (expectedTotal is not null && page.TotalHits != expectedTotal)
                {
                    throw new InvalidDataException("The workload feed changed or omitted its totalHits during discovery.");
                }

                if (page.RawCount < page.Items.Count || page.RawCount < 0 || page.RawCount > take
                    || (page.TotalHits is { } total && (total < skip + page.RawCount || (page.RawCount == 0 && skip < total))))
                {
                    throw new InvalidDataException("The workload feed returned inconsistent pagination metadata.");
                }

                expectedTotal ??= page.TotalHits;
                results.AddRange(page.Items);
                skip += page.RawCount;

                // Short pages may be server-clamped, and filtering can hide all
                // workloads. Without a total, only an empty raw page ends the scan.
                if (expectedTotal is { } totalHits ? skip == totalHits : page.RawCount == 0)
                {
                    reachedEnd = true;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidWorkloadSourceException ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new SetupConfigurationException(ex.Message, ex);
        }
        catch (InvalidDataException ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new SetupConfigurationException(
                $"Cannot complete setup stack discovery: {ex.Message} Correct the feed response or select another --source.", ex);
        }
        catch (Exception ex) when (
            ex is IOException
            or HttpRequestException
            or FatalProtocolException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Keep the offline fallback, but process the pages already received
            // so a later failure cannot erase known alias conflicts.
            useBuiltInFallback = true;
        }

        if (!useBuiltInFallback && !reachedEnd)
        {
            // Do not cache or plan from a truncated catalog: unseen packages may
            // contest any observed alias, including the built-in names.
            throw new SetupConfigurationException(
                "Setup stack discovery reached its scan limit before the feed ended. "
                + "Use a smaller --source feed, or install packages explicitly with 'func workload install --exact <package-id>'.");
        }

        // Offset paging cannot guarantee a stable snapshot of a mutable feed.
        // Raw identities are unavailable here, so identical parsed rows remain allowed.
        foreach (IGrouping<string, CatalogSearchResult> package in results.GroupBy(result => result.PackageId, StringComparer.OrdinalIgnoreCase))
        {
            CatalogSearchResult first = package.First();
            HashSet<string> aliases = new(first.Aliases, StringComparer.OrdinalIgnoreCase);
            string? canonical = string.Equals(first.Kind, StackKind, StringComparison.OrdinalIgnoreCase)
                && (first.Aliases.Count > 0 || first.CanonicalStack is not null)
                ? ResolveCanonicalStack(first) : first.CanonicalStack;
            if (package.Skip(1).Any(row => !string.Equals(row.Kind, first.Kind, StringComparison.OrdinalIgnoreCase)
                || !aliases.SetEquals(row.Aliases)
                || !string.Equals(canonical, string.Equals(row.Kind, StackKind, StringComparison.OrdinalIgnoreCase)
                    && (row.Aliases.Count > 0 || row.CanonicalStack is not null)
                    ? ResolveCanonicalStack(row) : row.CanonicalStack, StringComparison.OrdinalIgnoreCase)))
                throw new SetupConfigurationException($"Workload feed returned inconsistent metadata for package '{package.Key}'. Retry against a consistent --source.");
        }

        Dictionary<string, string> claims = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> templates = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> ambiguous = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> unsupported = new(StringComparer.OrdinalIgnoreCase);

        // Every alias competes for ownership, so conflicts have to be settled
        // before deciding which name is primary. A rogue package claiming
        // another's alternate spelling is the same attack as claiming its main one.
        foreach (CatalogSearchResult result in results)
        {
            bool isContent = string.Equals(result.Kind, "content", StringComparison.OrdinalIgnoreCase);
            bool isRidPointer = string.Equals(result.Kind, "rid-pointer", StringComparison.OrdinalIgnoreCase);
            bool isPortableRole = string.Equals(result.Kind, StackKind, StringComparison.OrdinalIgnoreCase)
                || (isContent && result.Aliases.Any(alias => alias.EndsWith(TemplatesAliasSuffix, StringComparison.OrdinalIgnoreCase)));
            bool isUnsupported = isRidPointer || (isPortableRole && !string.IsNullOrWhiteSpace(result.Rid)
                && !string.Equals(result.Rid, "any", StringComparison.OrdinalIgnoreCase));

            foreach (string alias in result.Aliases)
            {
                if (isUnsupported)
                {
                    unsupported.Add(alias);
                }

                // Ownership spans every kind, matching WorkloadPackageSource. A
                // stack reusing a worker or host alias has to collide here, or
                // setup would offer it and install it by exact id, never running
                // the check that would have refused the same alias by name.
                Claim(claims, ambiguous, alias, result.PackageId);

                if (isContent && alias.EndsWith(TemplatesAliasSuffix, StringComparison.OrdinalIgnoreCase))
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

            string primary = ResolveCanonicalStack(result);

            // Record alternate spellings before deciding the primary's fate. If
            // the primary is contested they still have to fold onto it, or the
            // alternate escapes as an unknown runtime, misses the ambiguity
            // check that only knows the primary, and half-installs.
            foreach (string alias in result.Aliases.Where(alias => !string.Equals(alias, primary, StringComparison.OrdinalIgnoreCase)))
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

        Dictionary<string, IReadOnlyList<SetupAliasConflict>> conflicts = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in ambiguous)
        {
            SetupAliasConflict[] details = [.. results
                .SelectMany(result => result.Aliases
                    .Where(alias => string.Equals(alias, name, StringComparison.OrdinalIgnoreCase)
                        || (string.Equals(result.Kind, "content", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(alias, name + TemplatesAliasSuffix, StringComparison.OrdinalIgnoreCase)))
                    .Select(alias => (Alias: alias, result.PackageId)))
                .GroupBy(claim => claim.Alias, StringComparer.OrdinalIgnoreCase)
                .Select(group => new SetupAliasConflict(group.Key, [.. group.Select(claim => claim.PackageId)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)]))
                .Where(conflict => conflict.PackageIds.Count > 1)
                .OrderBy(conflict => conflict.Alias, StringComparer.OrdinalIgnoreCase)];
            conflicts[name] = details;
        }

        if (!useBuiltInFallback && stacks.Count > 0)
        {
            return new SetupStackSnapshot(stacks, templates, ambiguous, secondary, conflicts, unsupported);
        }

        // Empty or failed discovery uses built-in ids. Observed conflicts,
        // unsupported roles and their alternate spellings still apply on fallback.
        // Uncontested aliases from partial results must not redirect built-in names.
        return ambiguous.Count == 0 && unsupported.Count == 0
            ? SetupDependency.BuiltInStackSnapshot
            : SetupDependency.BuiltInStackSnapshot with
            {
                AmbiguousAliases = ambiguous,
                AliasConflicts = conflicts,
                UnsupportedAliases = unsupported,
                SecondaryAliases = secondary
                    .Where(pair => ambiguous.Contains(pair.Value) || unsupported.Contains(pair.Value)
                        || ambiguous.Contains(pair.Value + TemplatesAliasSuffix) || unsupported.Contains(pair.Value + TemplatesAliasSuffix))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            };
    }

    /// <summary>
    /// Records an alias claim, flagging it as ambiguous when a second package
    /// claims the same alias with a different id. Mirrors the rejection
    /// <see cref="WorkloadPackageSource"/> applies to alias installs, rather
    /// than letting catalog ordering decide.
    /// </summary>
    private static void Claim(
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

            return;
        }

        map[alias] = packageId;
    }

    private static bool OwnedBy(Dictionary<string, string> claims, string alias, string packageId)
        => claims.TryGetValue(alias, out string? owner)
            && string.Equals(owner, packageId, StringComparison.OrdinalIgnoreCase);

    private static string ResolveCanonicalStack(CatalogSearchResult result)
    {
        string[] aliases = [.. result.Aliases.Distinct(StringComparer.OrdinalIgnoreCase)];
        if (result.CanonicalStack is null && aliases.Length == 1)
        {
            return aliases[0].ToLowerInvariant();
        }

        string? canonical = result.CanonicalStack;
        if (!string.IsNullOrWhiteSpace(canonical)
            && result.Aliases.Contains(canonical, StringComparer.OrdinalIgnoreCase))
        {
            return canonical.ToLowerInvariant();
        }

        throw new SetupConfigurationException(
            $"Stack package '{result.PackageId}' has invalid canonical stack metadata. "
            + "If present, 'stack:<canonical-alias>' must be non-empty, appear exactly once, and match a declared alias. "
            + "The tag may be omitted only when the package has one distinct alias. "
            + "Correct the package metadata or install it explicitly with 'func workload install --exact <package-id>'.");
    }
}
