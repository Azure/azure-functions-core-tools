// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed class TemplateGroup : IReadOnlyList<ResolvedTemplate>
{
    private readonly IReadOnlyList<ResolvedTemplate> _items;

    internal TemplateGroup(IReadOnlyList<ResolvedTemplate> items, IReadOnlyList<TemplateCatalogEntry> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(diagnostics);
        _items = Array.AsReadOnly<ResolvedTemplate>([.. items]);
        Diagnostics = Array.AsReadOnly<TemplateCatalogEntry>([.. diagnostics]);
    }

    public int Count => _items.Count;

    public ResolvedTemplate this[int index] => _items[index];

    public IReadOnlyList<TemplateCatalogEntry> Diagnostics { get; }

    public TemplateGroup FilterByLanguage(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        return new([.. _items.Where(item => string.Equals(item.Entry.Language, language, StringComparison.OrdinalIgnoreCase))], Diagnostics);
    }

    public TemplateGroup FilterByArguments(IReadOnlySet<string> matchingIdentities)
    {
        ArgumentNullException.ThrowIfNull(matchingIdentities);
        return new([.. _items.Where(item => matchingIdentities.Contains(item.Entry.Identity))], Diagnostics);
    }

    public TemplateGroup FilterToHighestPrecedence()
    {
        if (_items.Count == 0)
        {
            return new([], Diagnostics);
        }

        int highest = _items.Max(item => item.Entry.Precedence);
        return new([.. _items.Where(item => item.Entry.Precedence == highest)], Diagnostics);
    }

    public IEnumerator<ResolvedTemplate> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal enum TemplateResolutionStatus
{
    Success,
    NotFound,
    WrongType,
    Unavailable,
    AmbiguousGroup,
    InvalidTemplateType,
}

internal sealed record TemplateResolutionResult(TemplateResolutionStatus Status, TemplateGroup? Group, IReadOnlyList<TemplateCatalogEntry> Diagnostics, string? Diagnostic = null);