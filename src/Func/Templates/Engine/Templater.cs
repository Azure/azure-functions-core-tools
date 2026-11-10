// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;

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
