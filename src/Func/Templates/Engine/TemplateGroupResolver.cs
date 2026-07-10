// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Groups per-language template variants under a shared <c>groupIdentity</c> and
/// <c>shortName</c> and narrows a group to the winning variant for a resolved
/// language. This is the func analogue of the <c>dotnet new</c>
/// group-disambiguation mechanism, driven by the standard <c>language</c> tag.
/// <para>
/// Selection is by language alone; the stack of a selected template is always
/// <em>derived</em> from the winning variant's language via the registered
/// <see cref="IProjectInitializer"/> language declarations — there is no
/// hardcoded language-to-stack table. Narrowing a group by an explicit language
/// therefore yields the stack of whichever variant survives (e.g. selecting the
/// TypeScript variant derives the Node stack).
/// </para>
/// </summary>
internal sealed class TemplateGroupResolver
{
    /// <summary>
    /// Standard <c>Microsoft.TemplateEngine</c> language tag. This is the sole
    /// selection axis: variants are chosen by matching the requested language,
    /// and the stack is derived from the winning variant's language.
    /// </summary>
    public const string LanguageTag = "language";

    // Case-insensitive map of any accepted language token (canonical label,
    // alias, or a single-language stack's id) -> canonical language label.
    private readonly IReadOnlyDictionary<string, string> _toCanonicalLanguage;

    // Case-insensitive map of canonical language label -> canonical stack id.
    private readonly IReadOnlyDictionary<string, string> _canonicalLanguageToStack;

    public TemplateGroupResolver(IEnumerable<IProjectInitializer> initializers)
    {
        ArgumentNullException.ThrowIfNull(initializers);

        Dictionary<string, string> toCanonical = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> canonicalToStack = new(StringComparer.OrdinalIgnoreCase);

        foreach (IProjectInitializer initializer in initializers)
        {
            if (initializer is null)
            {
                continue;
            }

            string stack = initializer.Stack;
            IReadOnlyDictionary<string, IReadOnlyList<string>> aliases = initializer.SupportedLanguageAliases;

            foreach (KeyValuePair<string, IReadOnlyList<string>> entry in aliases)
            {
                string canonical = entry.Key;
                canonicalToStack.TryAdd(canonical, stack);
                toCanonical.TryAdd(canonical, canonical);
                foreach (string alias in entry.Value)
                {
                    if (!string.IsNullOrWhiteSpace(alias))
                    {
                        toCanonical.TryAdd(alias, canonical);
                    }
                }
            }

            // A single-language stack surfaces its language as the stack id (the
            // FunctionsProject.Language default), so accept the stack id as a
            // token for that one language. Multi-language stacks must be
            // disambiguated by the language label itself.
            if (aliases.Count == 1)
            {
                string canonical = aliases.Keys.First();
                toCanonical.TryAdd(stack, canonical);
            }
        }

        _toCanonicalLanguage = toCanonical;
        _canonicalLanguageToStack = canonicalToStack;
    }

    /// <summary>
    /// Partitions <paramref name="templates"/> into groups keyed by
    /// <see cref="ITemplateMetadata.GroupIdentity"/>. Templates without a
    /// group identity each form their own singleton group keyed by
    /// <see cref="ITemplateLocator.Identity"/>, so an ungrouped template still
    /// resolves like a group of one.
    /// </summary>
    public IReadOnlyList<TemplateGroup> BuildGroups(IEnumerable<ITemplateInfo> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);

        // Preserve first-seen order for stable, deterministic output.
        Dictionary<string, List<TemplateGroupVariant>> byGroup = new(StringComparer.OrdinalIgnoreCase);
        List<string> order = [];

        foreach (ITemplateInfo template in templates)
        {
            if (template is null)
            {
                continue;
            }

            string key = string.IsNullOrWhiteSpace(template.GroupIdentity)
                ? template.Identity
                : template.GroupIdentity;

            if (!byGroup.TryGetValue(key, out List<TemplateGroupVariant>? variants))
            {
                variants = [];
                byGroup[key] = variants;
                order.Add(key);
            }

            variants.Add(new TemplateGroupVariant(
                template,
                GetTag(template, LanguageTag)));
        }

        List<TemplateGroup> groups = new(order.Count);
        foreach (string key in order)
        {
            List<TemplateGroupVariant> variants = byGroup[key];
            List<string> shortNames = [];
            foreach (TemplateGroupVariant variant in variants)
            {
                foreach (string shortName in variant.Template.ShortNameList)
                {
                    if (!string.IsNullOrWhiteSpace(shortName)
                        && !shortNames.Contains(shortName, StringComparer.OrdinalIgnoreCase))
                    {
                        shortNames.Add(shortName);
                    }
                }
            }

            groups.Add(new TemplateGroup(key, shortNames, variants));
        }

        return groups;
    }

    /// <summary>
    /// Finds the group whose shared <c>shortName</c> matches
    /// <paramref name="shortName"/> (case-insensitive). Returns <c>false</c>
    /// when no group owns the requested short name.
    /// </summary>
    public bool TryFindGroup(
        IReadOnlyList<TemplateGroup> groups,
        string shortName,
        out TemplateGroup? group)
    {
        ArgumentNullException.ThrowIfNull(groups);

        group = null;
        if (string.IsNullOrWhiteSpace(shortName))
        {
            return false;
        }

        foreach (TemplateGroup candidate in groups)
        {
            if (candidate.ShortNames.Contains(shortName, StringComparer.OrdinalIgnoreCase))
            {
                group = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Narrows <paramref name="group"/> to a single winning variant, filtering
    /// by <paramref name="language"/> when supplied. Matching is
    /// case-insensitive and alias-aware: a request expressed as a canonical
    /// label, an alias, or a single-language stack id all match the variant
    /// carrying the equivalent language tag. A <c>null</c> or empty filter is
    /// not applied, so a single-variant group resolves without a language.
    /// <list type="bullet">
    /// <item><see cref="TemplateVariantResolution.Resolved"/> when exactly one
    /// variant survives; its <see cref="TemplateVariantResolution.Resolved.Stack"/>
    /// is derived from the surviving variant's language.</item>
    /// <item><see cref="TemplateVariantResolution.Ambiguous"/> when more than
    /// one variant survives (e.g. a group with multiple languages and no
    /// language filter) — the caller applies a further tiebreak.</item>
    /// <item><see cref="TemplateVariantResolution.NoMatch"/> when no variant
    /// fits the requested language.</item>
    /// </list>
    /// </summary>
    public TemplateVariantResolution Resolve(TemplateGroup group, string? language)
    {
        ArgumentNullException.ThrowIfNull(group);

        IEnumerable<TemplateGroupVariant> candidates = group.Variants;

        if (!string.IsNullOrWhiteSpace(language))
        {
            string requested = Normalize(language);
            candidates = candidates.Where(v =>
                string.Equals(Normalize(v.Language), requested, StringComparison.OrdinalIgnoreCase));
        }

        List<TemplateGroupVariant> surviving = [.. candidates];

        return surviving.Count switch
        {
            0 => new TemplateVariantResolution.NoMatch(),
            1 => new TemplateVariantResolution.Resolved(surviving[0], DeriveStack(surviving[0].Language)),
            _ => new TemplateVariantResolution.Ambiguous(surviving),
        };
    }

    // Reduces any accepted language token to its canonical label, falling back
    // to the token itself when it is not a registered language.
    private string Normalize(string language)
        => _toCanonicalLanguage.TryGetValue(language, out string? canonical) ? canonical : language;

    private string DeriveStack(string language)
        => _canonicalLanguageToStack.TryGetValue(Normalize(language), out string? stack) ? stack : string.Empty;

    private static string GetTag(ITemplateInfo template, string tag)
        => template.TagsCollection.TryGetValue(tag, out string? value) ? value : string.Empty;
}

/// <summary>
/// A single per-language variant within a <see cref="TemplateGroup"/>.
/// <paramref name="Language"/> is the variant's own <c>language</c> tag value
/// (empty when the tag is absent).
/// </summary>
internal sealed record TemplateGroupVariant(ITemplateInfo Template, string Language);

/// <summary>
/// A set of template variants that share a <c>groupIdentity</c> (or, for
/// ungrouped templates, an <see cref="ITemplateLocator.Identity"/>) and are
/// invoked by a common set of <c>shortName</c>s.
/// </summary>
internal sealed class TemplateGroup
{
    public TemplateGroup(
        string identity,
        IReadOnlyList<string> shortNames,
        IReadOnlyList<TemplateGroupVariant> variants)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        ShortNames = shortNames ?? throw new ArgumentNullException(nameof(shortNames));
        Variants = variants ?? throw new ArgumentNullException(nameof(variants));
    }

    /// <summary>The shared <c>groupIdentity</c>, or the identity of an ungrouped template.</summary>
    public string Identity { get; }

    /// <summary>The union of the <c>shortName</c>s the group's variants respond to.</summary>
    public IReadOnlyList<string> ShortNames { get; }

    /// <summary>The variants that make up the group.</summary>
    public IReadOnlyList<TemplateGroupVariant> Variants { get; }
}

/// <summary>
/// Outcome of narrowing a <see cref="TemplateGroup"/> to a winning variant.
/// </summary>
internal abstract record TemplateVariantResolution
{
    private TemplateVariantResolution()
    {
    }

    /// <summary>Exactly one variant survived; the winner and its derived stack.</summary>
    public sealed record Resolved(TemplateGroupVariant Variant, string Stack) : TemplateVariantResolution;

    /// <summary>More than one variant survived; a further tiebreak is required.</summary>
    public sealed record Ambiguous(IReadOnlyList<TemplateGroupVariant> Candidates) : TemplateVariantResolution;

    /// <summary>No variant fit the requested language.</summary>
    public sealed record NoMatch : TemplateVariantResolution;
}
