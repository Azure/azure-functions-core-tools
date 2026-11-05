// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Constraints;
using Microsoft.TemplateEngine.Abstractions.Mount;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Utils;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Command-scoped entry point to the template engine. Create it through <see cref="ITemplaterFactory"/> and dispose
/// it when the command is finished with templates.
/// </summary>
internal sealed class Templater(TemplateEngineSession session, InstalledProjectStackCatalog projectStacks) : IDisposable
{
    private readonly TemplateEngineSession _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly InstalledProjectStackCatalog _projectStacks = projectStacks ?? throw new ArgumentNullException(nameof(projectStacks));
    private readonly Dictionary<TemplateCatalogEntry, ITemplateInfo> _issuedEntries = new(ReferenceEqualityComparer.Instance);
    private readonly object _issuedEntriesLock = new();

    public TemplateEngineContext Context => _session.Context;

    /// <summary>
    /// Lists the installed templates of <paramref name="type"/>, including hidden templates and templates whose func
    /// host metadata can't be used.
    /// </summary>
    public async Task<IReadOnlyList<TemplateCatalogEntry>> ListAsync(TemplateType type, CancellationToken cancellationToken = default)
    {
        List<(ITemplateInfo Template, TemplateCatalogEntry? Entry)> candidates = (await LoadCatalogAsync(cancellationToken, type)).Candidates;
        List<TemplateCatalogEntry> entries = [];
        foreach ((ITemplateInfo template, TemplateCatalogEntry? entry) in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry?.Type != type)
            {
                continue;
            }

            RegisterIssuedEntry(entry, template);
            entries.Add(entry);
        }

        return entries.AsReadOnly();
    }

    /// <summary>
    /// Resolves an installed identity or short name without choosing between ambiguous candidates.
    /// </summary>
    /// <remarks>
    /// Selection does not pin content or authorize invocation. Any later invocation must validate the selected source
    /// and reject unsupported template features before performing effects.
    /// </remarks>
    public async Task<TemplateResolutionResult> ResolveAsync(string reference, TemplateType type, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        (List<(ITemplateInfo Template, TemplateCatalogEntry? Entry)> candidates, IReadOnlyList<ITemplatePackage> packages)
            = await LoadCatalogAsync(cancellationToken);
        Dictionary<string, List<(TemplateType? Type, string[] ShortNames, string Source)>> duplicates
            = await FindDuplicateTemplatesAsync(packages, cancellationToken);

        (ITemplateInfo Template, TemplateCatalogEntry? Entry)[] exactMatches = [.. candidates.Where(candidate =>
            string.Equals(candidate.Template.Identity, reference, StringComparison.OrdinalIgnoreCase))];
        if (duplicates.TryGetValue(reference, out List<(TemplateType? Type, string[] ShortNames, string Source)>? exactDuplicates)
            && exactDuplicates.Count > 1)
        {
            return DuplicateIdentityFailure(reference, exactDuplicates);
        }

        if (exactMatches.Length > 0)
        {
            if (exactMatches.Length > 1)
            {
                return DuplicateIdentityFailure(reference, [.. exactMatches.Select(match =>
                    (match.Entry?.Type, match.Template.ShortNameList.ToArray(), $"{match.Template.MountPointUri}::{match.Template.ConfigPlace}"))]);
            }

            TemplateCatalogEntry? entry = exactMatches[0].Entry;
            if (entry is null || entry.Type != type)
            {
                return Failure(
                    TemplateResolutionStatus.WrongKind,
                    new("wrong-kind", $"Template '{reference}' is not a {type.ToString().ToLowerInvariant()} template."));
            }

            return await ResolveExactAsync(exactMatches[0].Template, entry, cancellationToken);
        }

        KeyValuePair<string, List<(TemplateType? Type, string[] ShortNames, string Source)>>? duplicate = duplicates
            .Where(pair => pair.Value.Count > 1
                && pair.Value.Any(occurrence => occurrence.Type == type
                    && occurrence.ShortNames.Contains(reference, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => (KeyValuePair<string, List<(TemplateType? Type, string[] ShortNames, string Source)>>?)pair)
            .FirstOrDefault();
        if (duplicate is { } duplicateEntry)
        {
            return DuplicateIdentityFailure(duplicateEntry.Key, duplicateEntry.Value);
        }

        (ITemplateInfo Template, TemplateCatalogEntry? Entry)[] aliasMatches = [.. candidates.Where(candidate =>
            candidate.Template.ShortNameList.Contains(reference, StringComparer.OrdinalIgnoreCase))];
        if (aliasMatches.Length == 0)
        {
            return Failure(TemplateResolutionStatus.NotFound, new("not-found", $"No installed template matches '{reference}'."));
        }

        (ITemplateInfo Template, TemplateCatalogEntry Entry)[] kindMatches = [.. aliasMatches
            .Where(candidate => candidate.Entry?.Type == type)
            .Select(candidate => (candidate.Template, candidate.Entry!))];
        if (kindMatches.Length == 0)
        {
            return Failure(
                TemplateResolutionStatus.WrongKind,
                new("wrong-kind", $"Template alias '{reference}' does not identify a {type.ToString().ToLowerInvariant()} template."));
        }

        List<(ITemplateInfo Template, TemplateCatalogEntry Entry)> eligible = [];
        List<TemplateResolutionDiagnostic> diagnostics = [];
        foreach ((ITemplateInfo template, TemplateCatalogEntry entry) in kindMatches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TemplateResolutionDiagnostic? diagnostic = await CheckEligibilityAsync(template, entry, cancellationToken);
            if (diagnostic is null)
            {
                eligible.Add((template, entry));
            }
            else
            {
                diagnostics.Add(diagnostic);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (eligible.Count == 0)
        {
            return new(TemplateResolutionStatus.Ineligible, null, kindMatches.Select(candidate => candidate.Entry), diagnostics);
        }

        if (eligible.Count > 1)
        {
            int familyCount = eligible.Select(candidate => FamilyKey(candidate.Entry)).Distinct().Count();
            string message = familyCount > 1
                ? $"Template alias '{reference}' matches multiple eligible families."
                : $"Template alias '{reference}' matches multiple eligible variants in one family.";
            foreach ((ITemplateInfo template, TemplateCatalogEntry entry) in eligible)
            {
                RegisterIssuedEntry(entry, template);
            }

            return new(
                TemplateResolutionStatus.Ambiguous,
                null,
                eligible.Select(candidate => candidate.Entry),
                [new("ambiguous", message), .. diagnostics]);
        }

        (ITemplateInfo selectedTemplate, TemplateCatalogEntry selectedEntry) = eligible[0];
        return Selected(selectedTemplate, selectedEntry, diagnostics);
    }

    /// <summary>
    /// Resolves a concrete catalog entry issued by this templater without widening it to related variants.
    /// </summary>
    /// <remarks>
    /// Rechecking its locator does not prove that the template's metadata or assets are unchanged.
    /// </remarks>
    public async Task<TemplateResolutionResult> ResolveAsync(
        TemplateCatalogEntry selectedEntry,
        TemplateType type,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedEntry);
        _ = _session.PackageManager;
        cancellationToken.ThrowIfCancellationRequested();

        ITemplateInfo? issuedTemplate;
        lock (_issuedEntriesLock)
        {
            _issuedEntries.TryGetValue(selectedEntry, out issuedTemplate);
        }

        if (issuedTemplate is null)
        {
            return Failure(
                TemplateResolutionStatus.InvalidSelection,
                new("invalid-selection", "The catalog entry was not issued by this templater session.", selectedEntry.Identity));
        }

        (List<(ITemplateInfo Template, TemplateCatalogEntry? Entry)> candidates, IReadOnlyList<ITemplatePackage> packages)
            = await LoadCatalogAsync(cancellationToken);
        Dictionary<string, List<(TemplateType? Type, string[] ShortNames, string Source)>> duplicates
            = await FindDuplicateTemplatesAsync(packages, cancellationToken);
        if (duplicates.TryGetValue(selectedEntry.Identity, out List<(TemplateType? Type, string[] ShortNames, string Source)>? duplicateEntries)
            && duplicateEntries.Count > 1)
        {
            return DuplicateIdentityFailure(selectedEntry.Identity, duplicateEntries);
        }

        if (selectedEntry.Type != type)
        {
            return Failure(
                TemplateResolutionStatus.WrongKind,
                new("wrong-kind", $"Template '{selectedEntry.Identity}' is not a {type.ToString().ToLowerInvariant()} template.", selectedEntry.Identity));
        }

        (ITemplateInfo Template, TemplateCatalogEntry? Entry)[] currentMatches = [.. candidates.Where(candidate =>
            candidate.Entry is not null
            && string.Equals(candidate.Template.Identity, issuedTemplate.Identity, StringComparison.Ordinal)
            && string.Equals(candidate.Template.MountPointUri, issuedTemplate.MountPointUri, StringComparison.Ordinal)
            && string.Equals(candidate.Template.ConfigPlace, issuedTemplate.ConfigPlace, StringComparison.Ordinal)
            && candidate.Template.GeneratorId == issuedTemplate.GeneratorId)];
        if (currentMatches.Length != 1 || currentMatches[0].Entry is not { } currentEntry)
        {
            return Failure(
                TemplateResolutionStatus.InvalidSelection,
                new("stale-selection", $"Template '{selectedEntry.Identity}' is no longer available from the selected source.", selectedEntry.Identity));
        }

        return await ResolveExactAsync(currentMatches[0].Template, currentEntry, cancellationToken);
    }

    public void ValidateSelectionOwner(ResolvedTemplate selection) => _session.ValidateSelectionOwner(selection);

    public void Dispose()
    {
        lock (_issuedEntriesLock)
        {
            _issuedEntries.Clear();
            _session.Dispose();
        }
    }

    private async Task<(List<(ITemplateInfo Template, TemplateCatalogEntry? Entry)> Candidates, IReadOnlyList<ITemplatePackage> Packages)> LoadCatalogAsync(
        CancellationToken cancellationToken,
        TemplateType? projectionType = null)
    {
        TemplatePackageManager packageManager = _session.PackageManager;
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ITemplatePackage> packages = await packageManager.GetTemplatePackagesAsync(force: false, cancellationToken);
        IReadOnlyList<ITemplateInfo> templates = await packageManager.GetTemplatesAsync(cancellationToken);
        Dictionary<string, ITemplatePackage> packagesByMountPoint = new(StringComparer.Ordinal);
        foreach (ITemplatePackage package in packages)
        {
            packagesByMountPoint.TryAdd(package.MountPointUri, package);
        }

        List<(ITemplateInfo Template, TemplateCatalogEntry? Entry)> candidates = [];
        foreach (ITemplateInfo template in templates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TemplateType? templateType = TemplateCatalogProjector.GetTemplateType(template);
            TemplateCatalogEntry? entry = templateType is not null && (projectionType is null || templateType == projectionType)
                ? TemplateCatalogProjector.Project(template, packagesByMountPoint.GetValueOrDefault(template.MountPointUri))
                : null;
            candidates.Add((template, entry));
        }

        return (candidates, packages);
    }

    private async Task<Dictionary<string, List<(TemplateType? Type, string[] ShortNames, string Source)>>> FindDuplicateTemplatesAsync(
        IReadOnlyList<ITemplatePackage> packages,
        CancellationToken cancellationToken)
    {
        Scanner scanner = new(_session.Settings);
        Dictionary<string, List<(TemplateType? Type, string[] ShortNames, string Source)>> occurrences = new(StringComparer.OrdinalIgnoreCase);
        foreach (ITemplatePackage package in packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using ScanResult scan = await scanner.ScanAsync(
                package.MountPointUri,
                returnInvalidTemplates: true,
                cancellationToken: cancellationToken);
            foreach (IScanTemplateInfo template in scan.Templates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!occurrences.TryGetValue(template.Identity, out List<(TemplateType? Type, string[] ShortNames, string Source)>? templates))
                {
                    templates = [];
                    occurrences.Add(template.Identity, templates);
                }

                TemplateType? type = template.TagsCollection.TryGetValue("type", out string? typeTag)
                    ? typeTag?.ToLowerInvariant() switch
                    {
                        "item" => TemplateType.Item,
                        "project" => TemplateType.Project,
                        _ => null,
                    }
                    : null;
                templates.Add((type, [.. template.ShortNameList], $"{package.MountPointUri}::{template.ConfigPlace}"));
            }
        }

        return occurrences;
    }

    private static TemplateResolutionResult DuplicateIdentityFailure(
        string identity,
        IReadOnlyList<(TemplateType? Type, string[] ShortNames, string Source)> occurrences)
    {
        string[] sources = [.. occurrences.Select(occurrence => occurrence.Source).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        string sourceDetail = sources.Length == 0 ? string.Empty : $" Sources: {string.Join(", ", sources)}.";
        return Failure(
            TemplateResolutionStatus.DuplicateIdentity,
            new("duplicate-identity", $"Installed templates contain duplicate identity '{identity}'.{sourceDetail}", identity));
    }

    private async Task<TemplateResolutionResult> ResolveExactAsync(
        ITemplateInfo template,
        TemplateCatalogEntry entry,
        CancellationToken cancellationToken)
    {
        TemplateResolutionDiagnostic? diagnostic = await CheckEligibilityAsync(template, entry, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return diagnostic is null
            ? Selected(template, entry)
            : new(TemplateResolutionStatus.Ineligible, null, [entry], [diagnostic]);
    }

    private async Task<TemplateResolutionDiagnostic?> CheckEligibilityAsync(
        ITemplateInfo template,
        TemplateCatalogEntry entry,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (entry.MetadataDiagnostic is { } metadataDiagnostic)
        {
            return new("invalid-metadata", metadataDiagnostic, entry.Identity);
        }

        if (entry.IsHidden)
        {
            return new("hidden", $"Template '{entry.Identity}' is hidden and cannot be selected.", entry.Identity);
        }

        TemplateResolutionDiagnostic? projectDiagnostic = CheckProjectContext(out string? projectLanguage);
        if (projectDiagnostic is not null)
        {
            return projectDiagnostic with { TemplateIdentity = entry.Identity };
        }

        if (projectLanguage is { } language)
        {
            if (entry.Language is null)
            {
                return new("missing-language", $"Template '{entry.Identity}' does not declare a language.", entry.Identity);
            }

            if (!string.Equals(entry.Language, language, StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    "language-mismatch",
                    $"Template '{entry.Identity}' declares language '{entry.Language}', not '{language}'.",
                    entry.Identity);
            }
        }

        if (!_session.Settings.TryGetMountPoint(template.MountPointUri, out IMountPoint? mountPoint) || mountPoint is null)
        {
            return new("constraint-source-unavailable", $"Template '{entry.Identity}' source is unavailable for constraint validation.", entry.Identity);
        }

        using (mountPoint)
        {
            return await CheckMountedEligibilityAsync(template, entry, mountPoint, cancellationToken);
        }
    }

    private async Task<TemplateResolutionDiagnostic?> CheckMountedEligibilityAsync(
        ITemplateInfo template,
        TemplateCatalogEntry entry,
        IMountPoint mountPoint,
        CancellationToken cancellationToken)
    {
        IFile? configFile = mountPoint.FileInfo(template.ConfigPlace);
        if (configFile is null || !configFile.Exists)
        {
            return new("constraint-source-unavailable", $"Template '{entry.Identity}' configuration is missing.", entry.Identity);
        }

        string configuration;
        try
        {
            using Stream stream = configFile.OpenRead();
            using StreamReader reader = new(stream);
            configuration = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (IOException exception)
        {
            return new("constraint-source-unavailable", $"Template '{entry.Identity}' configuration could not be read: {exception.Message}", entry.Identity);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new("constraint-source-unavailable", $"Template '{entry.Identity}' configuration could not be read: {exception.Message}", entry.Identity);
        }

        IReadOnlyList<TemplateConstraintDeclaration> declarations;
        bool hasAdditionalConfiguration;
        try
        {
            declarations = TemplateConstraintDeclarationReader.Read(configuration, out hasAdditionalConfiguration);
        }
        catch (InvalidTemplateMetadataException exception)
        {
            return new("invalid-constraints", exception.Message, entry.Identity);
        }

        if (hasAdditionalConfiguration)
        {
            return new(
                "additional-configuration-not-evaluated",
                $"Template '{entry.Identity}' uses additional configuration files whose merged constraints cannot be verified.",
                entry.Identity);
        }

        if (declarations.Count != template.Constraints.Count
            || declarations.Where((declaration, index) =>
                !string.Equals(declaration.Type, template.Constraints[index].Type, StringComparison.Ordinal)).Any())
        {
            return new("constraint-metadata-mismatch", $"Template '{entry.Identity}' has constraint metadata the engine did not preserve.", entry.Identity);
        }

        FuncTemplateConstraintEvaluator funcEvaluator = new(new TemplateConstraintRequirementMatcher());
        IReadOnlyCollection<TemplateConstraintWorkload> workloads = [];
        string? bundleId = _session.Context.Bundle?.Id;
        NuGetVersion? bundleVersion = NuGetVersion.TryParse(_session.Context.Bundle?.Version, out NuGetVersion? parsedBundleVersion)
            ? parsedBundleVersion
            : null;

        for (int index = 0; index < declarations.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TemplateConstraintDeclaration declaration = declarations[index];
            if (declaration.Type == "func-workload")
            {
                return new(
                    "constraint-not-evaluated",
                    $"Template '{entry.Identity}' requires workload constraint '{declaration.Label}', but this context has no workload inventory.",
                    entry.Identity);
            }

            if (declaration.Type == "func-bundle")
            {
                FuncTemplateConstraintOutcome outcome = funcEvaluator.Evaluate(declaration, workloads, bundleId, bundleVersion);
                if (outcome != FuncTemplateConstraintOutcome.Eligible)
                {
                    return new(
                        outcome == FuncTemplateConstraintOutcome.Restricted ? "constraint-restricted" : "constraint-not-evaluated",
                        $"Template '{entry.Identity}' does not satisfy bundle constraint '{declaration.Label}'.",
                        entry.Identity);
                }

                continue;
            }

            TemplateConstraintInfo constraintInfo = template.Constraints[index];
            ITemplateConstraintFactory[] factories = [.. _session.Settings.Components
                .OfType<ITemplateConstraintFactory>()
                .Where(factory => string.Equals(factory.Type, constraintInfo.Type, StringComparison.Ordinal))];
            if (factories.Length != 1)
            {
                return new("constraint-not-evaluated", $"Template '{entry.Identity}' uses unsupported constraint '{constraintInfo.Type}'.", entry.Identity);
            }

            ITemplateConstraint constraint = await factories[0].CreateTemplateConstraintAsync(_session.Settings, cancellationToken);
            TemplateConstraintResult result = constraint.Evaluate(declaration.Arguments?.GetRawText());
            if (result.EvaluationStatus != TemplateConstraintResult.Status.Allowed)
            {
                return new(
                    result.EvaluationStatus == TemplateConstraintResult.Status.Restricted ? "constraint-restricted" : "constraint-not-evaluated",
                    result.LocalizedErrorMessage ?? $"Template '{entry.Identity}' constraint '{constraintInfo.Type}' could not be evaluated.",
                    entry.Identity);
            }
        }

        return null;
    }

    private TemplateResolutionDiagnostic? CheckProjectContext(out string? canonicalLanguage)
    {
        TemplateEngineProjectContext? project = _session.Context.Project;
        canonicalLanguage = null;
        if (project is null)
        {
            return null;
        }

        if (project.Stack is not { } stackName)
        {
            return new("missing-project-stack", "Resolve the project stack before selecting a template.");
        }

        IProjectStack? stack = _projectStacks.FindStack(stackName);
        if (stack is null)
        {
            return new("unsupported-project-stack", $"Project stack '{stackName}' is not available for template compatibility checks.");
        }

        if (project.Language is not { } language)
        {
            if (stack.SupportedLanguages.Count == 1)
            {
                canonicalLanguage = stack.SupportedLanguages[0];
                return null;
            }

            return new("missing-project-language", $"Project stack '{stack.Stack}' has no resolved language; resolve its language before selecting a template.");
        }

        canonicalLanguage = _projectStacks.GetCanonicalLanguage(language);
        return canonicalLanguage is not null && stack.SupportedLanguages.Contains(canonicalLanguage, StringComparer.OrdinalIgnoreCase)
            ? null
            : new("inconsistent-project-context", $"Project language '{language}' is not supported by stack '{stack.Stack}'.");
    }

    private void RegisterIssuedEntry(TemplateCatalogEntry entry, ITemplateInfo template)
    {
        lock (_issuedEntriesLock)
        {
            _ = _session.PackageManager;
            _issuedEntries.TryAdd(entry, template);
        }
    }

    private static (bool HasGroup, string Identity) FamilyKey(TemplateCatalogEntry entry)
        => entry.GroupIdentity is { Length: > 0 } groupIdentity ? (true, groupIdentity) : (false, entry.Identity);

    private TemplateResolutionResult Selected(
        ITemplateInfo template,
        TemplateCatalogEntry entry,
        IReadOnlyList<TemplateResolutionDiagnostic>? diagnostics = null)
    {
        RegisterIssuedEntry(entry, template);
        return new(TemplateResolutionStatus.Selected, new ResolvedTemplate(_session, entry, template), [entry], diagnostics ?? []);
    }

    private static TemplateResolutionResult Failure(TemplateResolutionStatus status, TemplateResolutionDiagnostic diagnostic)
        => new(status, null, [], [diagnostic]);

}
