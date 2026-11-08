// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Constraints;
using Microsoft.TemplateEngine.Abstractions.Mount;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge.Template;
using Microsoft.TemplateEngine.Utils;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Command-scoped entry point to the template engine. Create it through <see cref="ITemplaterFactory"/> and dispose
/// it when the command is finished with templates.
/// </summary>
/// <remarks>The templater and its session are intended for sequential use by one command.</remarks>
internal sealed class Templater(TemplateEngineSession session, InstalledProjectStackCatalog projectStacks) : IDisposable
{
    private readonly TemplateEngineSession _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly InstalledProjectStackCatalog _projectStacks = projectStacks ?? throw new ArgumentNullException(nameof(projectStacks));
    private readonly Dictionary<TemplateCatalogEntry, ITemplateInfo> _issuedEntries = new(ReferenceEqualityComparer.Instance);

    public TemplateEngineContext Context => _session.Context;

    /// <summary>
    /// Lists the installed templates of <paramref name="type"/>, including hidden templates and templates whose func
    /// host metadata can't be used.
    /// </summary>
    /// <exception cref="InvalidOperationException">The installed template inventory cannot be inspected completely.</exception>
    public async Task<IReadOnlyList<TemplateCatalogEntry>> ListAsync(TemplateType type, CancellationToken cancellationToken = default)
    {
        (List<(ITemplateInfo Template, TemplateCatalogEntry? Entry, string[] ShortNames)> candidates, _, TemplateResolutionDiagnostic? diagnostic)
            = await LoadCatalogAsync(cancellationToken, type);
        if (diagnostic is not null)
        {
            throw new InvalidOperationException(diagnostic.Message);
        }

        List<TemplateCatalogEntry> entries = [];
        foreach ((ITemplateInfo template, TemplateCatalogEntry? entry, string[] _) in candidates)
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
    /// Resolves an installed identity or short name. An ambiguous result returns eligible candidates for the caller
    /// to choose from; this method does not prompt or select one automatically.
    /// </summary>
    /// <remarks>
    /// Selection does not pin content or authorize invocation. Any later invocation must validate the selected source
    /// and reject unsupported template features before performing effects.
    /// </remarks>
    public async Task<TemplateResolutionResult> ResolveAsync(string reference, TemplateType type, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        (List<(ITemplateInfo Template, TemplateCatalogEntry? Entry, string[] ShortNames)> candidates,
            Dictionary<string, List<(TemplateType? Type, string[] ShortNames, string Source)>> duplicates,
            TemplateResolutionDiagnostic? inventoryDiagnostic) = await LoadCatalogAsync(cancellationToken, reference: reference);
        if (inventoryDiagnostic is not null)
        {
            return Failure(TemplateResolutionStatus.Failed, inventoryDiagnostic);
        }

        (ITemplateInfo Template, TemplateCatalogEntry? Entry, string[] ShortNames)[] exactMatches = [.. candidates.Where(candidate =>
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
                    (match.Entry?.Type, match.ShortNames, $"{match.Template.MountPointUri}::{match.Template.ConfigPlace}"))]);
            }

            TemplateCatalogEntry? entry = exactMatches[0].Entry;
            if (entry is null || entry.Type != type)
            {
                return Failure(
                    TemplateResolutionStatus.Failed,
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

        (ITemplateInfo Template, TemplateCatalogEntry? Entry, string[] ShortNames)[] aliasMatches = [.. candidates.Where(candidate =>
            candidate.ShortNames.Contains(reference, StringComparer.OrdinalIgnoreCase))];
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
                TemplateResolutionStatus.Failed,
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
            return new(TemplateResolutionStatus.Failed, null, kindMatches.Select(candidate => candidate.Entry), diagnostics);
        }

        if (eligible.Count > 1)
        {
            foreach ((ITemplateInfo template, TemplateCatalogEntry entry) in eligible)
            {
                RegisterIssuedEntry(entry, template);
            }

            return new(
                TemplateResolutionStatus.Ambiguous,
                null,
                eligible.Select(candidate => candidate.Entry),
                [new("ambiguous", $"Template alias '{reference}' matches multiple eligible templates."), .. diagnostics]);
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

        if (!_issuedEntries.TryGetValue(selectedEntry, out ITemplateInfo? issuedTemplate))
        {
            return Failure(
                TemplateResolutionStatus.Failed,
                new("invalid-selection", "The catalog entry was not issued by this templater session.", selectedEntry.Identity));
        }

        (List<(ITemplateInfo Template, TemplateCatalogEntry? Entry, string[] ShortNames)> candidates,
            Dictionary<string, List<(TemplateType? Type, string[] ShortNames, string Source)>> duplicates,
            TemplateResolutionDiagnostic? inventoryDiagnostic) = await LoadCatalogAsync(cancellationToken, reference: selectedEntry.Identity);
        if (inventoryDiagnostic is not null)
        {
            return Failure(TemplateResolutionStatus.Failed, inventoryDiagnostic);
        }
        if (duplicates.TryGetValue(selectedEntry.Identity, out List<(TemplateType? Type, string[] ShortNames, string Source)>? duplicateEntries)
            && duplicateEntries.Count > 1)
        {
            return DuplicateIdentityFailure(selectedEntry.Identity, duplicateEntries);
        }

        if (selectedEntry.Type != type)
        {
            return Failure(
                TemplateResolutionStatus.Failed,
                new("wrong-kind", $"Template '{selectedEntry.Identity}' is not a {type.ToString().ToLowerInvariant()} template.", selectedEntry.Identity));
        }

        (ITemplateInfo Template, TemplateCatalogEntry? Entry, string[] ShortNames)[] currentMatches = [.. candidates.Where(candidate =>
            candidate.Entry is not null
            && string.Equals(candidate.Template.Identity, issuedTemplate.Identity, StringComparison.Ordinal)
            && string.Equals(candidate.Template.MountPointUri, issuedTemplate.MountPointUri, StringComparison.Ordinal)
            && string.Equals(candidate.Template.ConfigPlace, issuedTemplate.ConfigPlace, StringComparison.Ordinal)
            && candidate.Template.GeneratorId == issuedTemplate.GeneratorId)];
        if (currentMatches.Length != 1 || currentMatches[0].Entry is not { } currentEntry)
        {
            return Failure(
                TemplateResolutionStatus.Failed,
                new("stale-selection", $"Template '{selectedEntry.Identity}' is no longer available from the selected source.", selectedEntry.Identity));
        }

        return await ResolveExactAsync(currentMatches[0].Template, currentEntry, cancellationToken);
    }

    public void Dispose()
    {
        _issuedEntries.Clear();
        _session.Dispose();
    }

    internal async Task<IReadOnlyList<(Guid ActionId, IReadOnlyDictionary<string, string> Arguments, bool ContinueOnError)>> PrepareTasksAsync(
        TemplateRenderResult result, IProjectTemplateTasks workload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(workload);
        List<(Guid ActionId, IReadOnlyDictionary<string, string> Arguments, bool ContinueOnError)> required = [];
        foreach ((Guid ActionId, ImmutableDictionary<string, string> Arguments, bool ContinueOnError) action in result.PostActions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (action.ActionId == new Guid("84C0DA21-51C8-4541-9940-6CA19AF04EE6"))
            {
                continue;
            }

            if (await workload.RequiresExecutionAsync(Context.CommandDirectory, action.ActionId, action.Arguments, cancellationToken))
            {
                required.Add((action.ActionId, action.Arguments, action.ContinueOnError));
            }
        }

        return required.AsReadOnly();
    }

    internal async Task<IReadOnlyList<string>> ExecuteTasksAsync(
        IReadOnlyList<(Guid ActionId, IReadOnlyDictionary<string, string> Arguments, bool ContinueOnError)> tasks,
        IProjectTemplateTasks workload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(workload);
        List<string> failures = [];
        foreach ((Guid actionId, IReadOnlyDictionary<string, string> arguments, bool continueOnError) in tasks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await workload.ExecuteAsync(Context.CommandDirectory, actionId, arguments, cancellationToken);
            }
            catch (GracefulException exception) when (continueOnError)
            {
                failures.Add(exception.Message);
            }
        }

        return failures.AsReadOnly();
    }

    /// <summary>
    /// Evaluates and generates the selected template against captured content without project writes or post-action execution.
    /// </summary>
    public async Task<TemplateRenderResult> RenderAsync(
        ResolvedTemplate selected, string name, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(parameters);
        cancellationToken.ThrowIfCancellationRequested();
        PrivateTemplateFileSystem memory = _session.PrivateFileSystem
            ?? throw new InvalidOperationException("Generation requires an operation-private captured-content session.");
        TemplateResolutionResult resolution = await ResolveAsync(selected.Descriptor, selected.Descriptor.Type, cancellationToken);
        if (resolution.Status != TemplateResolutionStatus.Selected
            || !_issuedEntries.TryGetValue(resolution.SelectedTemplate!.Descriptor, out ITemplateInfo? template))
        {
            throw new InvalidDataException($"The selected template cannot be generated: {string.Join("; ", resolution.Diagnostics.Select(diagnostic => diagnostic.Message))}");
        }

        foreach (string parameter in parameters.Keys)
        {
            if (!selected.Descriptor.Parameters.Any(definition => string.Equals(definition.CanonicalName, parameter, StringComparison.Ordinal)))
            {
                throw new ArgumentException($"Template '{selected.Descriptor.Identity}' does not accept parameter '{parameter}'.", nameof(parameters));
            }
        }

        TemplateCreator creator = new(_session.Settings);
        var input = parameters.ToDictionary(pair => pair.Key, pair => (string?)pair.Value, StringComparer.Ordinal);
        memory.BeginRendering(cancellationToken);
        ITemplateCreationResult result = await creator.InstantiateAsync(
            template, name, name, memory.OutputRoot, input, forceCreation: false, baselineName: null, dryRun: false, cancellationToken);
        memory.ThrowIfRenderFailed();
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Status != CreationResultStatus.Success || result.CreationResult is null)
        {
            throw new InvalidDataException($"Template generation failed ({result.Status}): {result.ErrorMessage}");
        }

        ImmutableDictionary<string, ImmutableArray<byte>>.Builder files = ImmutableDictionary.CreateBuilder<string, ImmutableArray<byte>>(StringComparer.Ordinal);
        foreach (string path in memory.EnumerateFiles(memory.OutputRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            files.Add(Path.GetRelativePath(memory.OutputRoot, path), [.. memory.ReadAllBytes(path)]);
        }

        ImmutableArray<string> directories = [.. memory.EnumerateDirectories(memory.OutputRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(memory.OutputRoot, path))];
        ImmutableArray<string> primaryOutputs = [.. result.CreationResult.PrimaryOutputs.Select(output =>
            Path.GetRelativePath(memory.OutputRoot, Path.Combine(memory.OutputRoot, output.Path)))];
        if (primaryOutputs.Any(path => !files.ContainsKey(path)))
        {
            throw new InvalidDataException("The template declares a primary output that was not generated privately.");
        }

        return new(selected.Descriptor.Identity, files.ToImmutable(), directories,
            [.. result.CreationResult.PostActions.Select(action =>
                (action.ActionId, action.Args.ToImmutableDictionary(StringComparer.Ordinal), action.ContinueOnError))]);
    }

    private async Task<(
        List<(ITemplateInfo Template, TemplateCatalogEntry? Entry, string[] ShortNames)> Candidates,
        Dictionary<string, List<(TemplateType? Type, string[] ShortNames, string Source)>> Occurrences,
        TemplateResolutionDiagnostic? Diagnostic)> LoadCatalogAsync(
        CancellationToken cancellationToken,
        TemplateType? projectionType = null,
        string? reference = null)
    {
        TemplatePackageManager packageManager = _session.PackageManager;
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ITemplatePackage> packages;
        IReadOnlyList<ITemplateInfo> templates;
        try
        {
            packages = await packageManager.GetTemplatePackagesAsync(force: false, cancellationToken);
            templates = await packageManager.GetTemplatesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return ([], new(StringComparer.OrdinalIgnoreCase), new(
                "installed-package-unavailable",
                $"Installed template packages could not be inspected: {exception.Message}"));
        }

        string? exactIdentity = reference is not null && templates.Any(template =>
            string.Equals(template.Identity, reference, StringComparison.OrdinalIgnoreCase))
                ? reference
                : null;
        (Dictionary<string, List<(TemplateType? Type, string[] ShortNames, string Source)>> occurrences,
            Dictionary<(string Mount, string Configuration, Guid Generator, string Identity), string[]> availableTemplates,
            TemplateResolutionDiagnostic? diagnostic) = await InspectInstalledTemplatesAsync(packages, templates, exactIdentity, cancellationToken);
        if (diagnostic is not null)
        {
            return ([], occurrences, diagnostic);
        }

        HashSet<string> cachedIdentities = new(templates.Select(template => template.Identity), StringComparer.OrdinalIgnoreCase);
        foreach ((string Mount, string Configuration, Guid Generator, string Identity) available in availableTemplates.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!cachedIdentities.Contains(available.Identity))
            {
                return ([], occurrences, new(
                    "installed-template-inventory-stale",
                    $"Installed template '{available.Identity}' is missing from the template catalog. Retry the command after repairing the installed package.",
                    available.Identity));
            }
        }

        Dictionary<string, ITemplatePackage> packagesByMountPoint = new(StringComparer.Ordinal);
        foreach (ITemplatePackage package in packages)
        {
            packagesByMountPoint.TryAdd(package.MountPointUri, package);
        }

        List<(ITemplateInfo Template, TemplateCatalogEntry? Entry, string[] ShortNames)> candidates = [];
        foreach (ITemplateInfo template in templates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!availableTemplates.TryGetValue((template.MountPointUri, template.ConfigPlace, template.GeneratorId, template.Identity), out string[]? shortNames))
            {
                return ([], occurrences, new(
                    "installed-template-unavailable",
                    $"Installed template '{template.Identity}' is no longer available from '{template.MountPointUri}::{template.ConfigPlace}'.",
                    template.Identity));
            }

            TemplateType? templateType = TemplateCatalogProjector.GetTemplateType(template);
            TemplateCatalogEntry? entry = templateType is not null && (projectionType is null || templateType == projectionType)
                ? TemplateCatalogProjector.Project(template, packagesByMountPoint.GetValueOrDefault(template.MountPointUri)) with { ShortNames = shortNames }
                : null;
            candidates.Add((template, entry, shortNames));
        }

        return (candidates, occurrences, null);
    }

    private async Task<(
        Dictionary<string, List<(TemplateType? Type, string[] ShortNames, string Source)>> Occurrences,
        Dictionary<(string Mount, string Configuration, Guid Generator, string Identity), string[]> AvailableTemplates,
        TemplateResolutionDiagnostic? Diagnostic)> InspectInstalledTemplatesAsync(
        IReadOnlyList<ITemplatePackage> packages,
        IReadOnlyList<ITemplateInfo> cachedTemplates,
        string? exactIdentity,
        CancellationToken cancellationToken)
    {
        Dictionary<string, List<(TemplateType? Type, string[] ShortNames, string Source)>> occurrences = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<(string Mount, string Configuration, Guid Generator, string Identity), string[]> availableTemplates = [];
        Dictionary<(string Mount, string Configuration, Guid Generator, string Identity), ITemplateInfo> cachedBySource = [];
        foreach (ITemplateInfo template in cachedTemplates)
        {
            cachedBySource.TryAdd((template.MountPointUri, template.ConfigPlace, template.GeneratorId, template.Identity), template);
        }

        foreach (ITemplatePackage package in packages.DistinctBy(package => package.MountPointUri, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!_session.Settings.TryGetMountPoint(package.MountPointUri, out IMountPoint? mountPoint) || mountPoint is null)
                {
                    return (occurrences, availableTemplates, new(
                        "installed-package-unavailable",
                        $"Installed template package '{package.MountPointUri}' is unavailable. Repair or reinstall the package."));
                }

                using (mountPoint)
                {
                    if (!mountPoint.Root.Exists)
                    {
                        return (occurrences, availableTemplates, new(
                            "installed-package-unavailable",
                            $"Installed template package '{package.MountPointUri}' is unavailable. Repair or reinstall the package."));
                    }

                    // The generator can log and omit a malformed configuration instead of returning it.
                    HashSet<string> configurations = new(StringComparer.Ordinal);
                    foreach (IFile file in mountPoint.Root.EnumerateFiles("template.json", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (string.Equals(file.Parent?.Name, ".template.config", StringComparison.OrdinalIgnoreCase))
                        {
                            configurations.Add(file.FullPath);
                        }
                    }

                    foreach (IGenerator generator in _session.Settings.Components.OfType<IGenerator>())
                    {
                        IReadOnlyList<IScanTemplateInfo> scannedTemplates =
                            await generator.GetTemplatesFromMountPointAsync(mountPoint, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        foreach (IScanTemplateInfo template in scannedTemplates)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            configurations.Remove(template.ConfigPlace);
                            if (!template.IsValid)
                            {
                                string? detail = template.ValidationErrors.FirstOrDefault()?.ErrorMessage;
                                return (occurrences, availableTemplates, new(
                                    "invalid-template-configuration",
                                    $"Installed template configuration '{package.MountPointUri}::{template.ConfigPlace}' is invalid{(detail is null ? "." : $": {detail}")}"));
                            }

                            if (!template.ShortNameList.Any(name => !string.IsNullOrWhiteSpace(name)))
                            {
                                return (occurrences, availableTemplates, new(
                                    "invalid-template-configuration",
                                    $"Installed template configuration '{package.MountPointUri}::{template.ConfigPlace}' must declare a non-empty shortName."));
                            }

                            (string Mount, string Configuration, Guid Generator, string Identity) source =
                                (package.MountPointUri, template.ConfigPlace, template.GeneratorId, template.Identity);
                            availableTemplates.TryAdd(source, [.. template.ShortNameList]);
                            if ((exactIdentity is null || string.Equals(exactIdentity, template.Identity, StringComparison.OrdinalIgnoreCase))
                                && cachedBySource.TryGetValue(source, out ITemplateInfo? cached))
                            {
                                string? changedMetadata = await GetChangedSelectionMetadataAsync(cached, template, mountPoint, cancellationToken);
                                if (changedMetadata is not null)
                                {
                                    return (occurrences, availableTemplates, new(
                                        "installed-template-inventory-stale",
                                        $"Installed template '{template.Identity}' has changed {changedMetadata} in '{package.MountPointUri}::{template.ConfigPlace}' since it was cached. Repair or reinstall the package, then retry.",
                                        template.Identity));
                                }
                            }

                            if (!occurrences.TryGetValue(template.Identity, out List<(TemplateType? Type, string[] ShortNames, string Source)>? matches))
                            {
                                matches = [];
                                occurrences.Add(template.Identity, matches);
                            }

                            TemplateType? type = template.TagsCollection.TryGetValue("type", out string? typeTag)
                                ? typeTag?.ToLowerInvariant() switch
                                {
                                    "item" => TemplateType.Item,
                                    "project" => TemplateType.Project,
                                    _ => null,
                                }
                                : null;
                            matches.Add((type, [.. template.ShortNameList], $"{package.MountPointUri}::{template.ConfigPlace}"));
                        }
                    }

                    if (configurations.Count > 0)
                    {
                        string configuration = configurations.Order(StringComparer.Ordinal).First();
                        return (occurrences, availableTemplates, new(
                            "invalid-template-configuration",
                            $"Installed template configuration '{package.MountPointUri}::{configuration}' could not be loaded. Repair or reinstall the package."));
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return (occurrences, availableTemplates, new(
                    "installed-package-unavailable",
                    $"Installed template package '{package.MountPointUri}' could not be inspected: {exception.Message}"));
            }
        }

        return (occurrences, availableTemplates, null);
    }

    private static async Task<string?> GetChangedSelectionMetadataAsync(
        ITemplateInfo cached,
        IScanTemplateInfo current,
        IMountPoint mountPoint,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(GetTag(cached, "type"), GetTag(current, "type"), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(GetTag(cached, "language"), GetTag(current, "language"), StringComparison.OrdinalIgnoreCase))
        {
            return "type or language metadata";
        }

        string? hostConfigPlace = null;
        string? hostData = null;
        if (current.HostConfigFiles.TryGetValue(FuncTemplateEngineHost.Identifier, out hostConfigPlace))
        {
            IFile? hostFile = mountPoint.FileInfo(hostConfigPlace);
            if (hostFile is null || !hostFile.Exists)
            {
                throw new FileNotFoundException($"Template host metadata '{hostConfigPlace}' is unavailable.");
            }

            using Stream stream = hostFile.OpenRead();
            using StreamReader reader = new(stream);
            hostData = await reader.ReadToEndAsync(cancellationToken);
        }

        FuncHostTemplateMetadata? cachedMetadata = ReadHostMetadata(
            cached.HostConfigPlace,
            (cached as ITemplateInfoHostJsonCache)?.HostData);
        FuncHostTemplateMetadata? currentMetadata = ReadHostMetadata(hostConfigPlace, hostData);
        if (cachedMetadata is null || currentMetadata is null)
        {
            // If both are invalid, the existing projection reports the metadata diagnostic.
            return cachedMetadata is null && currentMetadata is null ? null : "func.host.json metadata";
        }

        bool matches = cachedMetadata.IsHidden == currentMetadata.IsHidden
            && cachedMetadata.Symbols.Count == currentMetadata.Symbols.Count
            && cachedMetadata.Symbols.All(symbol =>
                currentMetadata.Symbols.TryGetValue(symbol.Key, out FuncHostSymbolMetadata? value) && symbol.Value == value);
        return matches ? null : "func.host.json metadata";
    }

    private static string? GetTag(ITemplateMetadata template, string name)
        => template.TagsCollection.TryGetValue(name, out string? value) && !string.IsNullOrEmpty(value) ? value : null;

    private static FuncHostTemplateMetadata? ReadHostMetadata(string? hostConfigPlace, string? hostData)
    {
        try
        {
            return FuncHostTemplateMetadataReader.Read(hostConfigPlace, hostData);
        }
        catch (InvalidTemplateMetadataException)
        {
            return null;
        }
    }

    private static TemplateResolutionResult DuplicateIdentityFailure(
        string identity,
        IReadOnlyList<(TemplateType? Type, string[] ShortNames, string Source)> occurrences)
    {
        string[] sources = [.. occurrences.Select(occurrence => occurrence.Source).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
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
        TemplateResolutionDiagnostic? diagnostic = await CheckEligibilityAsync(template, entry, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return diagnostic is null
            ? Selected(template, entry)
            : new(TemplateResolutionStatus.Failed, null, [entry], [diagnostic]);
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
        _ = _session.PackageManager;
        _issuedEntries.TryAdd(entry, template);
    }

    private TemplateResolutionResult Selected(
        ITemplateInfo template,
        TemplateCatalogEntry entry,
        IReadOnlyList<TemplateResolutionDiagnostic>? diagnostics = null)
    {
        RegisterIssuedEntry(entry, template);
        return new(TemplateResolutionStatus.Selected, new ResolvedTemplate(entry), [entry], diagnostics ?? []);
    }

    private static TemplateResolutionResult Failure(TemplateResolutionStatus status, TemplateResolutionDiagnostic diagnostic)
        => new(status, null, [], [diagnostic]);

}
