// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Settings;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Command-scoped entry point to the template engine. Create it through <see cref="ITemplaterFactory"/> and dispose
/// it when the command is finished with templates.
/// </summary>
internal sealed class Templater(TemplateEngineSession session) : IDisposable
{
    private readonly TemplateEngineSession _session = session ?? throw new ArgumentNullException(nameof(session));
    private IReadOnlyList<Candidate>? _candidates;

    public TemplateEngineContext Context => _session.Context;

    /// <summary>
    /// Lists the installed templates of <paramref name="type"/>, including hidden templates and templates whose func
    /// host metadata can't be used.
    /// </summary>
    public async Task<IReadOnlyList<TemplateCatalogEntry>> ListAsync(TemplateType type, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Candidate> candidates = await GetCandidatesAsync(cancellationToken);
        return Array.AsReadOnly<TemplateCatalogEntry>([.. candidates.Where(candidate => candidate.Entry.Type == type).Select(candidate => candidate.Entry)]);
    }

    public async Task<TemplateResolutionResult> ResolveGroupAsync(string reference, TemplateType type, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        IReadOnlyList<Candidate> candidates = await GetCandidatesAsync(cancellationToken);
        Candidate[] matches = [.. candidates.Where(candidate => string.Equals(candidate.Entry.Identity, reference, StringComparison.OrdinalIgnoreCase))];
        if (matches.Length == 0)
        {
            matches = [.. candidates.Where(candidate => candidate.Entry.ShortNames.Any(name => string.Equals(name, reference, StringComparison.OrdinalIgnoreCase)))];
        }

        if (matches.Length == 0)
        {
            return new(TemplateResolutionStatus.NotFound, null, []);
        }

        Candidate[] typed = [.. matches.Where(candidate => candidate.Entry.Type == type)];
        if (typed.Length == 0)
        {
            return new(TemplateResolutionStatus.WrongType, null, Array.AsReadOnly<TemplateCatalogEntry>([.. matches.Select(candidate => candidate.Entry)]));
        }

        IGrouping<string, Candidate>[] groups = [.. typed.GroupBy(candidate => candidate.Entry.GroupIdentity ?? candidate.Entry.Identity, StringComparer.Ordinal)];
        if (groups.Length != 1)
        {
            return new(TemplateResolutionStatus.AmbiguousGroup, null, Array.AsReadOnly<TemplateCatalogEntry>([.. typed.Select(candidate => candidate.Entry)]));
        }

        return CreateGroup([.. groups[0]]);
    }

    public async Task<TemplateResolutionResult> CreateGroupAsync(IReadOnlyList<TemplateCatalogEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        IReadOnlyList<Candidate> candidates = await GetCandidatesAsync(cancellationToken);
        List<Candidate> selected = [];
        foreach (TemplateCatalogEntry entry in entries)
        {
            Candidate? candidate = candidates.FirstOrDefault(candidate => ReferenceEquals(candidate.Entry, entry));
            if (candidate is null)
            {
                throw new ArgumentException("Group entries must originate from this template flow.", nameof(entries));
            }

            selected.Add(candidate);
        }

        if (selected.Select(candidate => candidate.Entry.Type).Distinct().Count() > 1
            || selected.Select(candidate => candidate.Entry.GroupIdentity ?? candidate.Entry.Identity).Distinct(StringComparer.Ordinal).Count() > 1)
        {
            return new(TemplateResolutionStatus.AmbiguousGroup, null, Array.AsReadOnly<TemplateCatalogEntry>([.. selected.Select(candidate => candidate.Entry)]));
        }

        return CreateGroup(selected);
    }

    private TemplateResolutionResult CreateGroup(IReadOnlyList<Candidate> candidates)
    {
        List<ResolvedTemplate> items = [];
        List<TemplateCatalogEntry> diagnostics = [];
        foreach (Candidate candidate in candidates.OrderBy(candidate => candidate.Entry.Identity, StringComparer.Ordinal))
        {
            if (candidate.Entry.ConstraintEvaluation.Status != TemplateConstraintEvaluationStatus.Eligible || candidate.Entry.MetadataDiagnostic is not null)
            {
                diagnostics.Add(candidate.Entry);
                continue;
            }

            items.Add(new(candidate.Entry, (request, token) => _session.InvokeAsync(candidate.Template, candidate.Configuration!, request, token)));
        }

        TemplateGroup group = new(items.AsReadOnly(), diagnostics.AsReadOnly());
        return new(items.Count > 0 ? TemplateResolutionStatus.Success : TemplateResolutionStatus.Unavailable, group, group.Diagnostics);
    }

    private async Task<IReadOnlyList<Candidate>> GetCandidatesAsync(CancellationToken cancellationToken)
    {
        TemplatePackageManager packageManager = _session.PackageManager;
        cancellationToken.ThrowIfCancellationRequested();
        if (_candidates is not null)
        {
            return _candidates;
        }

        IReadOnlyList<ITemplateInfo> templates = await packageManager.GetTemplatesAsync(cancellationToken);
        IReadOnlyList<ITemplatePackage> packages = await packageManager.GetTemplatePackagesAsync(force: false, cancellationToken);
        List<Candidate> candidates = [];
        foreach (ITemplateInfo template in templates.Where(template => TemplateCatalogProjector.GetTemplateType(template) is not null))
        {
            TemplateCatalogEntry entry = TemplateCatalogProjector.Project(template, packages.FirstOrDefault(package => package.MountPointUri == template.MountPointUri));
            entry = entry with
            {
                ShortNames = Array.AsReadOnly<string>([.. entry.ShortNames]),
                Parameters = Array.AsReadOnly<TemplateParameterDefinition>(
                    [.. entry.Parameters.Select(parameter => parameter with { Choices = Array.AsReadOnly<TemplateParameterChoice>([.. parameter.Choices]) })]),
            };

            try
            {
                TemplateConfigurationSnapshot configuration = await _session.ReadConfigurationAsync(template, cancellationToken);
                TemplateConstraintEvaluation evaluation = await _session.EvaluateConstraintsAsync(configuration.Content, cancellationToken);
                candidates.Add(new(template, entry with { ConstraintEvaluation = evaluation }, configuration));
            }
            catch (InvalidTemplateMetadataException exception)
            {
                candidates.Add(new(template, entry with
                {
                    ConstraintEvaluation = new(TemplateConstraintEvaluationStatus.Failed,
                        [new("template.json", "configuration", TemplateConstraintEvaluationStatus.Failed, exception.Message, "Correct the template configuration and rerun selection.")]),
                }, null));
            }
        }

        _candidates = candidates.AsReadOnly();
        return _candidates;
    }

    public void Dispose() => _session.Dispose();

    private sealed record Candidate(ITemplateInfo Template, TemplateCatalogEntry Entry, TemplateConfigurationSnapshot? Configuration);
}
