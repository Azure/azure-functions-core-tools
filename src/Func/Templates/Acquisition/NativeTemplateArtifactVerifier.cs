// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.TemplateEngine.Abstractions;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Acquisition;

internal sealed record NativeTemplateOwnerIdentity(string PackageId, string Version, string Source,
    string? LogicalPackageId = null, string? LogicalVersion = null, string? LogicalSource = null,
    WorkloadKind Kind = WorkloadKind.Workload, string? RuntimeIdentifier = null, string? AssemblyPath = null, string? EntryPointType = null)
{
    public static NativeTemplateOwnerIdentity FromEntry(WorkloadEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new(entry.PackageId, entry.PackageVersion, entry.Source,
            entry.LogicalPackage?.PackageId, entry.LogicalPackage?.PackageVersion, entry.LogicalPackage?.Source,
            entry.Kind, entry.RuntimeIdentifier, entry.EntryPoint?.AssemblyPath, entry.EntryPoint?.Type);
    }
}

/// <summary>
/// Carries coordinates only. Fresh authority, ownership and engine bytes must independently authorize every use.
/// </summary>
internal sealed record NativeTemplateSelection(string TemplateIdentity, TemplateArtifactIdentity Artifact, string Source,
    NativeTemplateOwnerIdentity Owner, BundleChannel? Channel);

/// <summary>
/// Joins current native bytes, durable provenance and qualified installed-workload associations without acquiring content.
/// </summary>
internal sealed class NativeTemplateArtifactVerifier(IBasicCompanionCatalog catalog, BasicAcquisitionPlanner planner,
    INativeTemplateOwnership ownership, IWorkloadStore workloads, IBasicArchiveInspector inspector)
{
    private readonly IBasicCompanionCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly BasicAcquisitionPlanner _planner = planner ?? throw new ArgumentNullException(nameof(planner));
    private readonly INativeTemplateOwnership _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
    private readonly IWorkloadStore _workloads = workloads ?? throw new ArgumentNullException(nameof(workloads));
    private readonly IBasicArchiveInspector _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    private string? _inventory;

    internal NativeTemplateArtifactVerifier ForInventory(IReadOnlyList<WorkloadEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return new(_catalog, _planner, _ownership, _workloads, _inspector) { _inventory = Inventory(entries) };
    }

    private static string Inventory(IReadOnlyList<WorkloadEntry> entries)
        => JsonSerializer.Serialize(new WorkloadRegistry
        {
            Workloads = [.. entries.OrderBy(entry => entry.PackageId, StringComparer.Ordinal).ThenBy(entry => entry.PackageVersion, StringComparer.Ordinal)],
        }, WorkloadJsonContext.Default.WorkloadRegistry);

    internal async Task<NativeTemplateSelection> ProjectAsync(TemplateEngineSession session, ITemplateInfo template,
        NativeTemplateOwnerIdentity owner, BundleChannel? channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(owner);
        TemplateArtifactIdentity current = await session.ReadCurrentArtifactAsync(template, cancellationToken)
            ?? throw Unavailable("The current template has no unique managed archive evidence.");
        NativeTemplateOwnerReceipt receipt = await _ownership.GetRetainedAsync(current.PackageId, cancellationToken)
            ?? throw Unavailable("The current template has no retained lifecycle provenance.");
        NativeTemplateSelection selection = new(template.Identity, current, receipt.Source, owner, channel);
        await VerifyAsync(session, template, selection, cancellationToken);
        return selection;
    }

    internal async Task VerifyAsync(TemplateEngineSession session, ITemplateInfo template, NativeTemplateSelection selection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(selection.Artifact);
        ArgumentNullException.ThrowIfNull(selection.Owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(selection.TemplateIdentity);
        session.ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(template.Identity, selection.TemplateIdentity, StringComparison.Ordinal))
        {
            throw Unavailable("The current full template identity differs from the selection.");
        }

        IReadOnlyList<WorkloadEntry> installed = await _workloads.GetWorkloadsAsync(cancellationToken);
        if (_inventory is not null && _inventory != Inventory(installed))
        {
            throw Unavailable("Installed workloads changed after the fresh capability scope was built. Rebuild the scope and select again.");
        }
        WorkloadEntry[] owners = [.. installed.Where(entry => NativeTemplateOwnerIdentity.FromEntry(entry) == selection.Owner)];
        if (owners.Length != 1)
        {
            throw Unavailable("The selected workload owner is missing, changed or ambiguous.");
        }
        WorkloadEntry owner = owners[0];
        BasicCompanionManifest manifest = await _catalog.LoadAsync(cancellationToken);
        string id = owner.LogicalPackage?.PackageId ?? owner.PackageId;
        if (!NuGetVersion.TryParse(owner.LogicalPackage?.PackageVersion ?? owner.PackageVersion, out NuGetVersion? version))
        {
            throw Unavailable("The selected workload owner has no valid version.");
        }
        BasicAcquisitionRequest request = new(id, version, session.Context.CommandDirectory, selection.Source)
        {
            IncludePrerelease = selection.Channel == BundleChannel.Preview,
            NativeContext = new(owner, FuncTemplateEngineHost.Identifier, NuGetVersion.Parse(AssemblyCliVersionProvider.Instance.Version),
                selection.Channel, [NativeTemplateContentRole.AdditionalProject]),
        };
        BasicAcquisitionPlan plan = _planner.Plan(manifest, request, installed, selection.Source)
            ?? throw Unavailable("No qualified current native association covers this selection.");
        BasicTemplateArtifact[] matches = [.. plan.Companions.SelectMany(companion => companion.Candidates).Where(artifact =>
            string.Equals(artifact.PackageId, selection.Artifact.PackageId, StringComparison.OrdinalIgnoreCase)
            && artifact.Version.ToNormalizedString() == selection.Artifact.Version
            && artifact.Source == selection.Source
            && string.Equals(artifact.Sha256, selection.Artifact.Sha256, StringComparison.OrdinalIgnoreCase)
            && artifact.ContentRoles.Any(role => role.Identity == selection.TemplateIdentity && role.Role == NativeTemplateContentRole.AdditionalProject))];
        if (matches.Length != 1)
        {
            throw Unavailable("The exact artifact and project identity have no unique qualified association.");
        }
        BasicTemplateArtifact authorized = matches[0];
        NativeTemplateOwnerReceipt receipt = await _ownership.GetRetainedAsync(authorized.PackageId, cancellationToken)
            ?? throw Unavailable("The authorized artifact has no retained lifecycle provenance.");
        if (!string.Equals(receipt.PackageId, authorized.PackageId, StringComparison.OrdinalIgnoreCase)
            || receipt.Version != authorized.Version.ToNormalizedString() || receipt.Source != authorized.Source
            || !string.Equals(receipt.Sha256, authorized.Sha256, StringComparison.OrdinalIgnoreCase)
            || receipt.ContentRevision != authorized.ContentRevision || receipt.PinnedVersion != receipt.Version)
        {
            throw Unavailable("Retained provenance differs from the authorized exact artifact.");
        }
        await session.ValidateCurrentArtifactAsync(template, authorized, _inspector, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static BasicAcquisitionException Unavailable(string diagnostic)
        => new(BasicAcquisitionFailure.OwnershipUnavailable, diagnostic);
}
