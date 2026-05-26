// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// The deserialized CDN manifest with convenience filter methods.
/// </summary>
internal sealed class QuickstartManifest
{
    /// <summary>
    /// All entries after trusted-org and IaC-only filtering has been applied.
    /// </summary>
    public IReadOnlyList<QuickstartEntry> Entries { get; }

    public QuickstartManifest(IReadOnlyList<QuickstartEntry> entries)
    {
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
    }

    /// <summary>
    /// Returns entries matching all supplied non-null/non-empty criteria.
    /// </summary>
    public IReadOnlyList<QuickstartEntry> Filter(
        string? language = null,
        string? resource = null,
        string? iac = null,
        string? search = null)
    {
        IEnumerable<QuickstartEntry> results = Entries;

        if (!string.IsNullOrWhiteSpace(language))
        {
            results = results.Where(t =>
                string.Equals(t.Language, language, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(resource))
        {
            results = results.Where(t =>
                string.Equals(t.Resource, resource, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(iac))
        {
            results = results.Where(t =>
                string.Equals(t.Iac, iac, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Case-insensitive substring match across searchable fields.
            results = results.Where(t => MatchesSearch(t, search));
        }

        return [.. results.OrderBy(t => t.Priority)];
    }

    private static bool MatchesSearch(QuickstartEntry entry, string search)
    {
        if (entry.Id.Contains(search, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (entry.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (entry.Resource.Contains(search, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (entry.ShortDescription is not null &&
            entry.ShortDescription.Contains(search, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return entry.Tags.Any(tag => tag.Contains(search, StringComparison.OrdinalIgnoreCase));
    }
}
