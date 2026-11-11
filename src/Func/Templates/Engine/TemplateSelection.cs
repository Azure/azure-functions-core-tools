// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Applies identity, alias, kind, and ambiguity policy to one command's candidate-bearing catalog.
/// </summary>
internal sealed class TemplateSelection(TemplateEngineSession session, TemplateEligibilityEvaluator eligibilityEvaluator)
{
    private readonly TemplateEngineSession _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly TemplateEligibilityEvaluator _eligibilityEvaluator =
        eligibilityEvaluator ?? throw new ArgumentNullException(nameof(eligibilityEvaluator));

    public async Task<TemplateResolutionResult> ResolveAsync(
        IReadOnlyList<TemplateCatalogEntry> catalog,
        string reference,
        TemplateType type,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        _session.ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        TemplateCatalogEntry[] exactMatches = [.. catalog.Where(entry =>
            string.Equals(entry.Identity, reference, StringComparison.OrdinalIgnoreCase))];
        if (exactMatches.Length > 1)
        {
            return DuplicateIdentityFailure(reference, exactMatches);
        }

        if (exactMatches.Length == 1)
        {
            TemplateCatalogEntry entry = exactMatches[0];
            if (entry.Type != type)
            {
                string article = type == TemplateType.Item ? "an" : "a";
                return Failure(Diagnostic(
                    entry,
                    "wrong-kind",
                    $"Template '{reference}' is not {article} {type.ToString().ToLowerInvariant()} template."));
            }

            if (!_session.TryGetTemplateInfo(entry, out ITemplateInfo? candidate))
            {
                return InvalidEntry(entry);
            }

            return await ResolveExactAsync(candidate, entry, cancellationToken);
        }

        TemplateCatalogEntry[] duplicateAlias = FindDuplicateAliasIdentities(catalog, reference, type);
        if (duplicateAlias.Length > 0)
        {
            return DuplicateIdentityFailure(duplicateAlias[0].Identity, duplicateAlias);
        }

        TemplateCatalogEntry[] aliasMatches = [.. catalog.Where(entry =>
            entry.ShortNames.Contains(reference, StringComparer.OrdinalIgnoreCase))];
        if (aliasMatches.Length == 0)
        {
            return new([], [new("not-found", $"No installed template matches '{reference}'.")]);
        }

        TemplateCatalogEntry[] kindMatches = [.. aliasMatches.Where(entry => entry.Type == type)];
        if (kindMatches.Length == 0)
        {
            string article = type == TemplateType.Item ? "an" : "a";
            TemplateResolutionDiagnostic[] diagnostics = [.. aliasMatches.Select(entry =>
                Diagnostic(
                    entry,
                    "wrong-kind",
                    $"Template alias '{reference}' does not identify {article} {type.ToString().ToLowerInvariant()} template."))];
            return new([], diagnostics);
        }

        List<TemplateCatalogEntry> eligible = [];
        List<TemplateResolutionDiagnostic> diagnosticsForRejected = [];
        foreach (TemplateCatalogEntry entry in kindMatches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_session.TryGetTemplateInfo(entry, out ITemplateInfo? candidate))
            {
                return InvalidEntry(entry);
            }

            TemplateEligibilityResult result = await _eligibilityEvaluator.EvaluateAsync(candidate, entry, cancellationToken);
            if (result.IsEligible)
            {
                eligible.Add(entry);
            }
            else
            {
                diagnosticsForRejected.AddRange(result.Diagnostics);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (eligible.Count == 0)
        {
            return new([], [.. diagnosticsForRejected]);
        }

        if (eligible.Count > 1)
        {
            return new(
                [.. eligible],
                [new("ambiguous", $"Template alias '{reference}' matches multiple eligible templates."), .. diagnosticsForRejected]);
        }

        return new([eligible[0]], [.. diagnosticsForRejected]);
    }

    public async Task<TemplateResolutionResult> ResolveAsync(
        IReadOnlyList<TemplateCatalogEntry> catalog,
        TemplateCatalogEntry selectedEntry,
        TemplateType type,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(selectedEntry);
        _session.ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (!_session.TryGetTemplateInfo(selectedEntry, out ITemplateInfo? template))
        {
            return InvalidEntry(selectedEntry);
        }

        TemplateCatalogEntry[] duplicates = [.. catalog.Where(entry =>
            string.Equals(entry.Identity, selectedEntry.Identity, StringComparison.OrdinalIgnoreCase))];
        if (duplicates.Length > 1)
        {
            return DuplicateIdentityFailure(selectedEntry.Identity, duplicates);
        }

        if (selectedEntry.Type != type)
        {
            string article = type == TemplateType.Item ? "an" : "a";
            return Failure(Diagnostic(
                selectedEntry,
                "wrong-kind",
                $"Template '{selectedEntry.Identity}' is not {article} {type.ToString().ToLowerInvariant()} template."));
        }

        return await ResolveExactAsync(template, selectedEntry, cancellationToken);
    }

    private static TemplateCatalogEntry[] FindDuplicateAliasIdentities(
        IReadOnlyList<TemplateCatalogEntry> catalog,
        string reference,
        TemplateType type)
    {
        foreach (IGrouping<string, TemplateCatalogEntry> group in catalog
            .GroupBy(entry => entry.Identity, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1
                && group.Any(entry => entry.Type == type
                    && entry.ShortNames.Contains(reference, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            return [.. group];
        }

        return [];
    }

    private TemplateResolutionResult DuplicateIdentityFailure(
        string identity,
        IReadOnlyList<TemplateCatalogEntry> entries)
    {
        TemplateResolutionDiagnostic[] diagnostics = [.. entries
            .Select(entry =>
            {
                string? source = _session.TryGetTemplateInfo(entry, out ITemplateInfo? candidate)
                    ? $"{candidate.MountPointUri}::{candidate.ConfigPlace}"
                    : null;
                return (Entry: entry, Source: source);
            })
            .OrderBy(item => item.Source, StringComparer.Ordinal)
            .ThenBy(item => item.Entry.Name, StringComparer.Ordinal)
            .Select(item =>
        {
            TemplateCatalogEntry entry = item.Entry;
            return new TemplateResolutionDiagnostic(
                "duplicate-identity",
                $"Installed templates contain duplicate identity '{identity}'.",
                identity,
                entry.Package?.Identifier,
                TemplateName: entry.Name,
                TemplateKind: entry.Type,
                TemplateSource: item.Source);
        })];
        return new([], diagnostics);
    }

    private async Task<TemplateResolutionResult> ResolveExactAsync(
        ITemplateInfo template,
        TemplateCatalogEntry entry,
        CancellationToken cancellationToken)
    {
        TemplateEligibilityResult result = await _eligibilityEvaluator.EvaluateAsync(template, entry, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result.IsEligible
            ? new([entry], [])
            : new([], [.. result.Diagnostics]);
    }

    private static TemplateResolutionResult InvalidEntry(TemplateCatalogEntry entry)
        => Failure(new(
            "invalid-selection",
            "The catalog entry does not contain a valid engine candidate owned by this live templater session.",
            entry.Identity,
            entry.Package?.Identifier,
            entry.MetadataDiagnosticCause,
            entry.Name,
            entry.Type));

    private static TemplateResolutionDiagnostic Diagnostic(
        TemplateCatalogEntry entry,
        string code,
        string message,
        Exception? cause = null)
        => new(code, message, entry.Identity, entry.Package?.Identifier, cause, entry.Name, entry.Type);

    private static TemplateResolutionResult Failure(TemplateResolutionDiagnostic diagnostic)
        => new([], [diagnostic]);
}
