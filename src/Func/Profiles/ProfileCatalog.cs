// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Versioning;

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Default profile catalog over project, user, and built-in profile sources.
/// </summary>
internal sealed class ProfileCatalog(IEnumerable<IProfileSource> sources) : IProfileCatalog
{
    private const int MaxInheritanceDepth = 5;

    private readonly IReadOnlyList<IProfileSource> _sources =
        (sources ?? throw new ArgumentNullException(nameof(sources))).ToList();

    public async Task<IReadOnlyList<ProfileSourceSnapshot>> LoadAsync(
        ProfileSourceContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<ProfileSourceSnapshot> snapshots = [];
        foreach (IProfileSource source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            snapshots.Add(await source.LoadAsync(context, cancellationToken));
        }

        return snapshots;
    }

    public IReadOnlyList<ProfileDefinitionEntry> ListEffectiveProfiles(
        IReadOnlyList<ProfileSourceSnapshot> snapshots,
        IReadOnlySet<ProfileSourceKind>? sourceKinds = null)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        List<ProfileDefinitionEntry> entries = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (ProfileSourceSnapshot snapshot in snapshots)
        {
            if (sourceKinds is not null && !sourceKinds.Contains(snapshot.Source.Kind))
            {
                continue;
            }

            foreach ((string name, ProfileDefinition definition) in snapshot.Profiles
                .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (seen.Add(name))
                {
                    entries.Add(new ProfileDefinitionEntry(name, definition, snapshot.Source));
                }
            }
        }

        return [.. entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public ProfileDefinitionEntry? FindProfile(string name, IReadOnlyList<ProfileSourceSnapshot> snapshots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(snapshots);

        return FindProfileCore(name, snapshots);
    }

    public ResolvedProfile ResolveProfile(string name, IReadOnlyList<ProfileSourceSnapshot> snapshots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(snapshots);

        ProfileDefinitionEntry entry = FindProfileCore(name, snapshots)
            ?? throw new ProfileConfigurationException($"Profile '{name}' was not found.");

        return ResolveProfile(entry, snapshots);
    }

    public ResolvedProfile ResolveProfile(
        ProfileDefinitionEntry entry,
        IReadOnlyList<ProfileSourceSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(snapshots);

        ProfileDefinitionEntry resolvedEntry = ResolveDefinitionEntry(entry, snapshots, []);
        VersionRange hostVersionRange = ParseRequiredRange(
            resolvedEntry.Definition.Host?.Version,
            resolvedEntry.Name,
            "host.version");
        Dictionary<string, VersionRange> workerRanges = new(StringComparer.OrdinalIgnoreCase);
        if (resolvedEntry.Definition.Workers is { } workers)
        {
            foreach ((string runtime, ProfileWorkerConstraint? constraint) in workers)
            {
                if (constraint is null)
                {
                    continue;
                }

                workerRanges[runtime] = ParseRequiredRange(
                    constraint.Version,
                    resolvedEntry.Name,
                    $"workers.{runtime}.version");
            }
        }

        VersionRange? bundleRange = resolvedEntry.Definition.ExtensionBundle?.Version is { } extensionBundleVersion
            ? ParseRequiredRange(extensionBundleVersion, resolvedEntry.Name, "extensionBundle.version")
            : null;

        ProfileStatus status = resolvedEntry.Definition.Status is null
            ? ProfileStatus.Stable
            : ProfileDocumentParser.ParseStatus(
                resolvedEntry.Definition.Status,
                resolvedEntry.Name,
                resolvedEntry.Source.DisplayName);

        return new ResolvedProfile(
            resolvedEntry.Name,
            resolvedEntry.Source,
            resolvedEntry.Definition.Sku,
            status,
            resolvedEntry.Definition.DeprecationUrl,
            hostVersionRange,
            workerRanges,
            bundleRange,
            resolvedEntry.Definition.SupportedRuntimes,
            resolvedEntry.Definition.Notes);
    }

    private ProfileDefinitionEntry ResolveDefinitionEntry(
        ProfileDefinitionEntry entry,
        IReadOnlyList<ProfileSourceSnapshot> snapshots,
        IReadOnlyList<string> chain)
    {
        if (chain.Count > MaxInheritanceDepth)
        {
            throw new ProfileConfigurationException($"Profile inheritance chain exceeds maximum depth of {MaxInheritanceDepth}.");
        }

        string? repeated = chain.FirstOrDefault(p => string.Equals(p, entry.Name, StringComparison.OrdinalIgnoreCase));
        if (repeated is not null)
        {
            string cycle = string.Join(" -> ", [.. chain, entry.Name]);
            throw new ProfileConfigurationException($"Circular profile inheritance detected: {cycle}.");
        }

        if (NullIfWhiteSpace(entry.Definition.Extends) is not { } parentName)
        {
            return entry;
        }

        ProfileDefinitionEntry parent = FindProfileCore(parentName, snapshots)
            ?? throw new ProfileConfigurationException($"Profile '{parentName}' was not found.");
        ProfileDefinitionEntry resolvedParent = ResolveDefinitionEntry(parent, snapshots, [.. chain, entry.Name]);
        ProfileDefinition merged = Merge(resolvedParent.Definition, entry.Definition);
        return entry with { Definition = merged };
    }

    private static ProfileDefinitionEntry? FindProfileCore(string name, IReadOnlyList<ProfileSourceSnapshot> snapshots)
    {
        foreach (ProfileSourceSnapshot snapshot in snapshots)
        {
            if (snapshot.Profiles.TryGetValue(name, out ProfileDefinition? definition))
            {
                return new ProfileDefinitionEntry(name, definition, snapshot.Source);
            }
        }

        return null;
    }

    private static ProfileDefinition Merge(ProfileDefinition parent, ProfileDefinition child)
        => new()
        {
            Sku = child.Sku ?? parent.Sku,
            Status = child.Status ?? parent.Status,
            DeprecationUrl = child.DeprecationUrl ?? parent.DeprecationUrl,
            Host = child.Host ?? parent.Host,
            Workers = MergeWorkers(parent.Workers, child.Workers),
            ExtensionBundle = child.ExtensionBundle ?? parent.ExtensionBundle,
            SupportedRuntimes = child.SupportedRuntimes ?? parent.SupportedRuntimes,
            Notes = child.Notes ?? parent.Notes,
        };

    private static Dictionary<string, ProfileWorkerConstraint?>? MergeWorkers(
        Dictionary<string, ProfileWorkerConstraint?>? parent,
        Dictionary<string, ProfileWorkerConstraint?>? child)
    {
        if (parent is null && child is null)
        {
            return null;
        }

        Dictionary<string, ProfileWorkerConstraint?> merged = parent is null
            ? new(StringComparer.OrdinalIgnoreCase)
            : new(parent, StringComparer.OrdinalIgnoreCase);

        if (child is not null)
        {
            foreach ((string runtime, ProfileWorkerConstraint? constraint) in child)
            {
                if (constraint is null)
                {
                    merged.Remove(runtime);
                }
                else
                {
                    merged[runtime] = constraint;
                }
            }
        }

        return merged;
    }

    private static VersionRange ParseRequiredRange(string? value, string profileName, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value) || !VersionRange.TryParse(value, out VersionRange? range))
        {
            throw new ProfileConfigurationException(
                $"Profile '{profileName}' has invalid NuGet version range for '{propertyName}'.");
        }

        return range;
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
