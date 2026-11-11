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
        IReadOnlyList<TemplateCatalogEntry> catalog,
        string reference,
        TemplateType type,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        _session.ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        ITemplateInfo[] exactMatches = [.. templates.Where(template =>
            string.Equals(template.Identity, reference, StringComparison.OrdinalIgnoreCase))];
        if (exactMatches.Length > 1)
        {
            return DuplicateIdentityFailure(reference, exactMatches, catalog);
        }

        if (exactMatches.Length == 1)
        {
            ITemplateInfo exactMatch = exactMatches[0];
            TemplateCatalogEntry? entry = FindEntry(catalog, exactMatch);
            if (entry is null || TemplateCatalogProjector.GetTemplateType(exactMatch) != type)
            {
                string article = type == TemplateType.Item ? "an" : "a";
                return Failure(new(
                    "wrong-kind",
                    $"Template '{reference}' is not {article} {type.ToString().ToLowerInvariant()} template.",
                    exactMatch.Identity,
                    TemplateName: exactMatch.Name,
                    TemplateKind: TemplateCatalogProjector.GetTemplateType(exactMatch)));
            }

            if (!_session.TryGetTemplateInfo(entry, out ITemplateInfo? candidate))
            {
                return InvalidEntry(entry);
            }

            return await ResolveExactAsync(candidate, entry, cancellationToken);
        }

        ITemplateInfo[] duplicateAlias = [.. FindDuplicateAliasIdentities(templates, reference, type)];
        if (duplicateAlias.Length > 0)
        {
            return DuplicateIdentityFailure(duplicateAlias[0].Identity, duplicateAlias, catalog);
        }

        ITemplateInfo[] aliasMatches = [.. templates.Where(template =>
            template.ShortNameList.Contains(reference, StringComparer.OrdinalIgnoreCase))];
        if (aliasMatches.Length == 0)
        {
            return new([], [new("not-found", $"No installed template matches '{reference}'.")]);
        }

        List<TemplateCatalogEntry> kindMatches = [];
        foreach (ITemplateInfo template in aliasMatches.Where(candidate =>
            TemplateCatalogProjector.GetTemplateType(candidate) == type))
        {
            TemplateCatalogEntry? entry = FindEntry(catalog, template);
            if (entry is null || !_session.TryGetTemplateInfo(entry, out _))
            {
                return Failure(new(
                    "invalid-selection",
                    "An installed template entry is missing its valid session ownership or engine candidate.",
                    template.Identity,
                    TemplateName: template.Name,
                    TemplateKind: TemplateCatalogProjector.GetTemplateType(template)));
            }

            kindMatches.Add(entry);
        }

        if (kindMatches.Count == 0)
        {
            string article = type == TemplateType.Item ? "an" : "a";
            TemplateResolutionDiagnostic[] diagnostics = [.. aliasMatches.Select(template =>
            {
                TemplateCatalogEntry? entry = FindEntry(catalog, template);
                return entry is null
                    ? new(
                        "wrong-kind",
                        $"Template alias '{reference}' does not identify {article} {type.ToString().ToLowerInvariant()} template.",
                        template.Identity,
                        TemplateName: template.Name,
                        TemplateKind: TemplateCatalogProjector.GetTemplateType(template))
                    : Diagnostic(
                        entry,
                        "wrong-kind",
                        $"Template alias '{reference}' does not identify {article} {type.ToString().ToLowerInvariant()} template.");
            })];
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
        IReadOnlyList<ITemplateInfo> templates,
        IReadOnlyList<TemplateCatalogEntry> catalog,
        TemplateCatalogEntry selectedEntry,
        TemplateType type,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(selectedEntry);
        _session.ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (!_session.TryGetTemplateInfo(selectedEntry, out ITemplateInfo? template))
        {
            return InvalidEntry(selectedEntry);
        }

        ITemplateInfo[] duplicates = [.. templates.Where(candidate =>
            string.Equals(candidate.Identity, selectedEntry.Identity, StringComparison.OrdinalIgnoreCase))];
        if (duplicates.Length > 1)
        {
            return DuplicateIdentityFailure(selectedEntry.Identity, duplicates, catalog);
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

    private static TemplateCatalogEntry? FindEntry(
        IReadOnlyList<TemplateCatalogEntry> catalog,
        ITemplateInfo template)
        => catalog.FirstOrDefault(entry => ReferenceEquals(entry.EngineTemplate, template));

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

    private static TemplateResolutionResult DuplicateIdentityFailure(
        string identity,
        IReadOnlyList<ITemplateInfo> templates,
        IReadOnlyList<TemplateCatalogEntry> catalog)
    {
        TemplateResolutionDiagnostic[] diagnostics = [.. templates
            .OrderBy(template => $"{template.MountPointUri}::{template.ConfigPlace}", StringComparer.Ordinal)
            .Select(template =>
        {
            TemplateCatalogEntry? entry = FindEntry(catalog, template);
            return new TemplateResolutionDiagnostic(
                "duplicate-identity",
                $"Installed templates contain duplicate identity '{identity}'.",
                identity,
                entry?.Package?.Identifier,
                TemplateName: entry?.Name ?? template.Name,
                TemplateKind: entry?.Type ?? TemplateCatalogProjector.GetTemplateType(template),
                TemplateSource: $"{template.MountPointUri}::{template.ConfigPlace}");
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
