// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Owns the command-scoped template session, installed inventory, and CLI catalog projection.
/// </summary>
/// <remarks>The templater and its session are intended for sequential use by one command.</remarks>
internal sealed class Templater(TemplateEngineSession session, TemplateSelection selection) : IDisposable
{
    private readonly TemplateEngineSession _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly TemplateSelection _selection = selection ?? throw new ArgumentNullException(nameof(selection));
    private Dictionary<TemplateCatalogEntry, ITemplateInfo> _issuedEntries = new(ReferenceEqualityComparer.Instance);
    private Dictionary<ITemplateInfo, TemplateCatalogEntry> _entries = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<ITemplateInfo>? _templates;
    private IReadOnlyList<TemplateCatalogEntry>? _catalog;

    public TemplateEngineContext Context => _session.Context;

    /// <summary>
    /// Lists installed templates of the requested type, including hidden templates and templates with unusable
    /// Functions host metadata.
    /// </summary>
    public async Task<IReadOnlyList<TemplateCatalogEntry>> ListAsync(TemplateType type, CancellationToken cancellationToken = default)
    {
        await EnsureInventoryAsync(cancellationToken);
        return Array.AsReadOnly(_catalog!.Where(entry => entry.Type == type).ToArray());
    }

    /// <summary>
    /// Delegates installed identity or short-name matching to the selection policy.
    /// </summary>
    public async Task<TemplateResolutionResult> ResolveAsync(
        string reference,
        TemplateType type,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        try
        {
            await EnsureInventoryAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return InventoryUnavailable(exception);
        }

        return await _selection.ResolveAsync(
            _templates!,
            _entries,
            _issuedEntries,
            reference,
            type,
            cancellationToken);
    }

    /// <summary>
    /// Delegates an exact catalog entry issued by this templater to the selection policy.
    /// </summary>
    public async Task<TemplateResolutionResult> ResolveAsync(
        TemplateCatalogEntry selectedEntry,
        TemplateType type,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedEntry);
        _session.ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureInventoryAsync(cancellationToken);
        return await _selection.ResolveAsync(
            _templates!,
            _entries,
            _issuedEntries,
            selectedEntry,
            type,
            cancellationToken);
    }

    public void Dispose()
    {
        _issuedEntries = new(ReferenceEqualityComparer.Instance);
        _entries = new(ReferenceEqualityComparer.Instance);
        _catalog = null;
        _templates = null;
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
        ITemplateInfo template = selected.GetTemplate(_session);

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

    private async Task EnsureInventoryAsync(CancellationToken cancellationToken)
    {
        if (_catalog is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        (IReadOnlyList<ITemplatePackage> packages, IReadOnlyList<ITemplateInfo> engineTemplates) =
            await _session.GetInventoryAsync(cancellationToken);
        IReadOnlyList<ITemplateInfo> templates = DistinctCandidates(engineTemplates, cancellationToken);

        Dictionary<string, ITemplatePackage> packagesByMountPoint = new(StringComparer.Ordinal);
        foreach (ITemplatePackage package in packages)
        {
            packagesByMountPoint.TryAdd(package.MountPointUri, package);
        }

        Dictionary<ITemplateInfo, TemplateCatalogEntry> entries = new(ReferenceEqualityComparer.Instance);
        Dictionary<TemplateCatalogEntry, ITemplateInfo> issuedEntries = new(ReferenceEqualityComparer.Instance);
        List<TemplateCatalogEntry> catalog = [];
        foreach (ITemplateInfo template in templates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TemplateCatalogProjector.GetTemplateType(template) is not { } type)
            {
                continue;
            }

            TemplateCatalogEntry entry = TemplateCatalogProjector.Project(
                template,
                packagesByMountPoint.GetValueOrDefault(template.MountPointUri));
            entries.Add(template, entry);
            issuedEntries.Add(entry, template);
            catalog.Add(entry);
        }

        _templates = templates;
        _entries = entries;
        _issuedEntries = issuedEntries;
        _catalog = catalog.AsReadOnly();
    }

    private static IReadOnlyList<ITemplateInfo> DistinctCandidates(
        IEnumerable<ITemplateInfo> candidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        HashSet<(string Mount, string Configuration, Guid Generator, string Identity)> sources = [];
        List<ITemplateInfo> distinct = [];
        foreach (ITemplateInfo candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sources.Add((candidate.MountPointUri, candidate.ConfigPlace, candidate.GeneratorId, candidate.Identity)))
            {
                distinct.Add(candidate);
            }
        }

        return Array.AsReadOnly(distinct.ToArray());
    }

    private static TemplateResolutionResult InventoryUnavailable(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new(
            TemplateResolutionStatus.Failed,
            null,
            [],
            [new("inventory-unavailable", $"Installed template inventory could not be inspected: {exception.Message}", Cause: exception)]);
    }
}
