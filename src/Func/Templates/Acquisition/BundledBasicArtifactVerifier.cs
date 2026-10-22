// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Acquisition;

/// <summary>
/// Binds Basic content to its actual bundled workload, independently of external publisher associations.
/// </summary>
internal sealed record BundledBasicSelection(string TemplateIdentity, NativeTemplateOwnerIdentity Owner,
    BundledTemplateOrigin Origin, BasicTemplateArtifact Declaration)
{
    public BasicTemplateArtifact Declaration { get; } = Declaration ?? throw new ArgumentNullException(nameof(Declaration));

    public TemplateArtifactIdentity Artifact => new(Declaration.PackageId, Declaration.Version.ToNormalizedString(), Declaration.Sha256);
}

internal sealed class BundledBasicArtifactVerifier(IBundledTemplateInstaller bundles, IWorkloadStore workloads, IFileSystem fileSystem)
{
    private readonly IBundledTemplateInstaller _bundles = bundles ?? throw new ArgumentNullException(nameof(bundles));
    private readonly IWorkloadStore _workloads = workloads ?? throw new ArgumentNullException(nameof(workloads));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private string? _inventory;
    private IReadOnlyDictionary<NativeTemplateOwnerIdentity, string> _runtimeDirectories = new Dictionary<NativeTemplateOwnerIdentity, string>();

    internal BundledBasicArtifactVerifier ForInventory(IReadOnlyList<WorkloadEntry> entries,
        IReadOnlyDictionary<NativeTemplateOwnerIdentity, string> runtimeDirectories)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(runtimeDirectories);
        return new(_bundles, _workloads, _fileSystem)
        {
            _inventory = WorkloadStateSnapshot.Capture(entries),
            _runtimeDirectories = new Dictionary<NativeTemplateOwnerIdentity, string>(runtimeDirectories),
        };
    }

    internal async Task<BundledBasicSelection?> ProjectAsync(TemplateEngineSession session, ITemplateInfo template,
        NativeTemplateOwnerIdentity owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(owner);
        session.ThrowIfDisposed();
        (BundledTemplateOrigin? origin, IReadOnlyList<BasicTemplateArtifact> declarations) = await ReadCurrentAsync(owner, cancellationToken);
        if (origin is null)
        {
            return null;
        }
        TemplateArtifactIdentity? current = await session.ReadCurrentArtifactAsync(template, cancellationToken);
        BasicTemplateArtifact[] matches = [.. declarations.Where(artifact => artifact.Templates.Any(definition =>
            definition.Identity == template.Identity && definition.Type == "project"))];
        if (matches.Length == 0)
        {
            return null;
        }
        if (matches.Length != 1 || current is null)
        {
            throw Invalid("The bundled project has no unique current managed archive.");
        }
        BundledBasicSelection selection = new(template.Identity, owner, origin, matches[0]);
        await VerifyAsync(session, template, selection, cancellationToken);
        return selection;
    }

    internal async Task VerifyAsync(TemplateEngineSession session, ITemplateInfo template, BundledBasicSelection selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(selection.Owner);
        ArgumentNullException.ThrowIfNull(selection.Origin);
        ArgumentException.ThrowIfNullOrWhiteSpace(selection.TemplateIdentity);
        session.ThrowIfDisposed();
        if (template.Identity != selection.TemplateIdentity)
        {
            throw Invalid("The bundled project identity changed after selection.");
        }
        (BundledTemplateOrigin? origin, IReadOnlyList<BasicTemplateArtifact> declarations) = await ReadCurrentAsync(selection.Owner, cancellationToken);
        BasicTemplateArtifact[] matches = [.. declarations.Where(artifact => BasicAcquisitionApproval.Same(artifact, selection.Declaration)
            && artifact.Templates.Any(definition => definition.Identity == selection.TemplateIdentity && definition.Type == "project"))];
        if (origin is null || origin != selection.Origin || matches.Length != 1)
        {
            throw Invalid("The live bundled origin or complete declaration changed after selection.");
        }
        BasicTemplateArtifact evidence = await _bundles.GetValidatedEvidenceAsync(session, origin, matches[0], cancellationToken);
        TemplateArtifactIdentity? current = await session.ReadCurrentArtifactAsync(template, cancellationToken);
        if (current is null || !string.Equals(current.PackageId, evidence.PackageId, StringComparison.OrdinalIgnoreCase)
            || current.Version != evidence.Version.ToNormalizedString()
            || !string.Equals(current.Sha256, evidence.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("The bundled selection does not name the current exact archive bytes.");
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task<(BundledTemplateOrigin? Origin, IReadOnlyList<BasicTemplateArtifact> Declarations)> ReadCurrentAsync(
        NativeTemplateOwnerIdentity owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        IReadOnlyList<WorkloadEntry> entries = await _workloads.GetWorkloadsAsync(cancellationToken);
        if (_inventory is null || _inventory != WorkloadStateSnapshot.Capture(entries)
            || entries.Count(entry => NativeTemplateOwnerIdentity.FromEntry(entry) == owner) != 1
            || !_runtimeDirectories.TryGetValue(owner, out string? directory))
        {
            throw Invalid("The bundled owner is not the unique live runtime in the fresh unchanged workload scope.");
        }
        WorkloadEntry entry = entries.Single(entry => NativeTemplateOwnerIdentity.FromEntry(entry) == owner);
        string? json = await _fileSystem.ReadAllTextIfExistsAsync(Path.Combine(directory, BundledTemplateInstaller.OriginName), cancellationToken);
        bool declared = entry.BundledTemplatesDeclared || _fileSystem.FileExists(Path.Combine(directory, BundledTemplateInstaller.ManifestName));
        if (json is null && !declared)
        {
            return (null, []);
        }
        if (json is null || !declared || entry.Kind != WorkloadKind.Workload)
        {
            throw Invalid("The live bundled workload declaration or origin is missing. Request explicit repair.");
        }
        BundledTemplateOrigin origin;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || document.RootElement.EnumerateObject().GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1))
            {
                throw Invalid("The bundled origin is malformed.");
            }
            origin = JsonSerializer.Deserialize(json, BundledTemplateJsonContext.Default.BundledTemplateOrigin)
                ?? throw Invalid("The bundled origin is missing.");
        }
        catch (JsonException exception)
        {
            throw new InvalidWorkloadException("The bundled origin is malformed. Request explicit repair.", exception);
        }
        if (origin.PackageId != owner.PackageId || origin.Version != owner.Version || origin.Source != owner.Source
            || origin.LogicalPackageId != owner.LogicalPackageId || origin.LogicalVersion != owner.LogicalVersion
            || origin.LogicalSource != owner.LogicalSource)
        {
            throw Invalid("The bundled physical or logical origin differs from the actual loaded workload.");
        }
        return (origin, await _bundles.ReadDeclarationsAsync(directory, origin, cancellationToken));
    }

    private static InvalidWorkloadException Invalid(string message) => new($"Bundled Basic binding failed: {message}");
}
