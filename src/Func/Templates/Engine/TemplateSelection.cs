// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Applies identity, alias, kind, and ambiguity policy to one command's installed-template inventory.
/// </summary>
internal sealed class TemplateSelection(TemplateEngineSession session, TemplateEligibilityEvaluator eligibilityEvaluator)
{
    private readonly TemplateEngineSession _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly TemplateEligibilityEvaluator _eligibilityEvaluator =
        eligibilityEvaluator ?? throw new ArgumentNullException(nameof(eligibilityEvaluator));

    public async Task<TemplateResolutionResult> ResolveAsync(
        IReadOnlyList<ITemplateInfo> templates,
        IReadOnlyDictionary<ITemplateInfo, TemplateCatalogEntry> entries,
        IReadOnlyDictionary<TemplateCatalogEntry, ITemplateInfo> issuedEntries,
        string reference,
        TemplateType type,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(issuedEntries);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        _session.ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        ITemplateInfo[] exactMatches = [.. templates.Where(template =>
            string.Equals(template.Identity, reference, StringComparison.OrdinalIgnoreCase))];
        if (exactMatches.Length > 1)
        {
            return DuplicateIdentityFailure(reference, exactMatches);
        }

        if (exactMatches.Length == 1)
        {
            ITemplateInfo exactMatch = exactMatches[0];
            entries.TryGetValue(exactMatch, out TemplateCatalogEntry? entry);
            if (entry is null || TemplateCatalogProjector.GetTemplateType(exactMatch) != type)
            {
                string message = $"Template '{reference}' is not a {type.ToString().ToLowerInvariant()} template.";
                TemplateResolutionDiagnostic diagnostic = entry is null
                    ? new("wrong-kind", message, reference)
                    : Diagnostic(entry, "wrong-kind", message);
                return Failure(TemplateResolutionStatus.Failed, diagnostic, entry is null ? null : [entry]);
            }

            return await ResolveExactAsync(exactMatch, entry, cancellationToken);
        }

        ITemplateInfo[] duplicateAlias = [.. FindDuplicateAliasIdentities(templates, reference, type)];
        if (duplicateAlias.Length > 0)
        {
            return DuplicateIdentityFailure(duplicateAlias[0].Identity, duplicateAlias);
        }

        ITemplateInfo[] aliasMatches = [.. templates.Where(template =>
            template.ShortNameList.Contains(reference, StringComparer.OrdinalIgnoreCase))];
        if (aliasMatches.Length == 0)
        {
            return Failure(TemplateResolutionStatus.NotFound, new("not-found", $"No installed template matches '{reference}'."));
        }

        TemplateCatalogEntry[] kindMatches = [.. aliasMatches
            .Where(template => TemplateCatalogProjector.GetTemplateType(template) == type)
            .Select(template => entries.GetValueOrDefault(template))
            .Where(entry => entry is not null)
            .Select(entry => entry!)];
        if (kindMatches.Length == 0)
        {
            return Failure(
                TemplateResolutionStatus.Failed,
                new("wrong-kind", $"Template alias '{reference}' does not identify a {type.ToString().ToLowerInvariant()} template."),
                [.. aliasMatches.Where(entries.ContainsKey).Select(template => entries[template])]);
        }

        List<TemplateCatalogEntry> eligible = [];
        List<TemplateResolutionDiagnostic> diagnostics = [];
        foreach (TemplateCatalogEntry entry in kindMatches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TemplateEligibilityResult result = await _eligibilityEvaluator.EvaluateAsync(
                issuedEntries[entry],
                entry,
                cancellationToken);
            if (result.IsEligible)
            {
                eligible.Add(entry);
            }
            else
            {
                diagnostics.AddRange(result.Diagnostics);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (eligible.Count == 0)
        {
            return new(TemplateResolutionStatus.Failed, null, kindMatches, diagnostics);
        }

        if (eligible.Count > 1)
        {
            return new(
                TemplateResolutionStatus.Ambiguous,
                null,
                eligible,
                [new("ambiguous", $"Template alias '{reference}' matches multiple eligible templates."), .. diagnostics]);
        }

        TemplateCatalogEntry selected = eligible[0];
        ITemplateInfo selectedTemplate = issuedEntries[selected];
        return Selected(selectedTemplate, selected, diagnostics);
    }

    public async Task<TemplateResolutionResult> ResolveAsync(
        IReadOnlyList<ITemplateInfo> templates,
        IReadOnlyDictionary<ITemplateInfo, TemplateCatalogEntry> entries,
        IReadOnlyDictionary<TemplateCatalogEntry, ITemplateInfo> issuedEntries,
        TemplateCatalogEntry selectedEntry,
        TemplateType type,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(issuedEntries);
        ArgumentNullException.ThrowIfNull(selectedEntry);
        _session.ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (!issuedEntries.TryGetValue(selectedEntry, out ITemplateInfo? template))
        {
            return Failure(TemplateResolutionStatus.Failed, new(
                "invalid-selection",
                "The catalog entry was not issued by this templater session.",
                selectedEntry.Identity,
                selectedEntry.Package?.Identifier));
        }

        ITemplateInfo[] duplicates = [.. templates.Where(candidate =>
            string.Equals(candidate.Identity, selectedEntry.Identity, StringComparison.OrdinalIgnoreCase))];
        if (duplicates.Length > 1)
        {
            return DuplicateIdentityFailure(selectedEntry.Identity, duplicates);
        }

        if (selectedEntry.Type != type)
        {
            return Failure(TemplateResolutionStatus.Failed, Diagnostic(
                selectedEntry,
                "wrong-kind",
                $"Template '{selectedEntry.Identity}' is not a {type.ToString().ToLowerInvariant()} template."));
        }

        if (!entries.TryGetValue(template, out TemplateCatalogEntry? issuedEntry) || !ReferenceEquals(issuedEntry, selectedEntry))
        {
            return Failure(TemplateResolutionStatus.Failed, new(
                "invalid-selection",
                "The catalog entry is not part of this templater session's installed inventory.",
                selectedEntry.Identity,
                selectedEntry.Package?.Identifier));
        }

        return await ResolveExactAsync(template, selectedEntry, cancellationToken);
    }

    private static IEnumerable<ITemplateInfo> FindDuplicateAliasIdentities(
        IReadOnlyList<ITemplateInfo> templates,
        string reference,
        TemplateType type)
    {
        foreach (IGrouping<string, ITemplateInfo> group in templates
            .GroupBy(template => template.Identity, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1
                && group.Any(template => TemplateCatalogProjector.GetTemplateType(template) == type
                    && template.ShortNameList.Contains(reference, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            foreach (ITemplateInfo template in group)
            {
                yield return template;
            }

            yield break;
        }
    }

    private static TemplateResolutionResult DuplicateIdentityFailure(string identity, IReadOnlyList<ITemplateInfo> templates)
    {
        string[] sources = [.. templates
            .Select(template => $"{template.MountPointUri}::{template.ConfigPlace}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
        string sourceDetail = sources.Length == 0 ? string.Empty : $" Sources: {string.Join(", ", sources)}.";
        return Failure(
            TemplateResolutionStatus.Failed,
            new("duplicate-identity", $"Installed templates contain duplicate identity '{identity}'.{sourceDetail}", identity));
    }

    private async Task<TemplateResolutionResult> ResolveExactAsync(
        ITemplateInfo template,
        TemplateCatalogEntry entry,
        CancellationToken cancellationToken)
    {
        TemplateEligibilityResult result = await _eligibilityEvaluator.EvaluateAsync(template, entry, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result.IsEligible
            ? Selected(template, entry)
            : new(TemplateResolutionStatus.Failed, null, [entry], result.Diagnostics);
    }

    private TemplateResolutionResult Selected(
        ITemplateInfo template,
        TemplateCatalogEntry entry,
        IReadOnlyList<TemplateResolutionDiagnostic>? diagnostics = null)
        => new(TemplateResolutionStatus.Selected, new ResolvedTemplate(_session, entry, template), [], diagnostics ?? []);

    private static TemplateResolutionDiagnostic Diagnostic(
        TemplateCatalogEntry entry,
        string code,
        string message,
        Exception? cause = null)
        => new(code, message, entry.Identity, entry.Package?.Identifier, cause);

    private static TemplateResolutionResult Failure(
        TemplateResolutionStatus status,
        TemplateResolutionDiagnostic diagnostic,
        IEnumerable<TemplateCatalogEntry>? candidates = null)
        => new(status, null, candidates ?? [], [diagnostic]);
}
