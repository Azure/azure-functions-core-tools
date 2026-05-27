// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// The deserialized CDN manifest with convenience filter methods.
/// </summary>
public sealed class QuickstartManifest
{
    /// <summary>
    /// All entries after validation and trusted-org filtering has been applied.
    /// </summary>
    public IReadOnlyList<QuickstartEntry> Entries { get; }

    internal QuickstartManifest(IReadOnlyList<QuickstartEntry> entries)
    {
        Entries = [.. (entries ?? throw new ArgumentNullException(nameof(entries)))
            .OrderBy(e => e.Priority)];
    }

    /// <summary>
    /// Returns entries matching all supplied non-null/non-empty criteria,
    /// ordered by <see cref="QuickstartEntry.Priority"/>.
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
            results = results.Where(e =>
                string.Equals(e.Language, language, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(resource))
        {
            results = results.Where(e =>
                string.Equals(e.Resource, resource, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(iac))
        {
            results = results.Where(e =>
                string.Equals(e.Iac, iac, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            results = results.Where(e => MatchesSearch(e, search));
        }

        return [.. results];
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

        if (entry.Iac is not null &&
            entry.Iac.Contains(search, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (entry.LongDescription is not null &&
            entry.LongDescription.Contains(search, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
