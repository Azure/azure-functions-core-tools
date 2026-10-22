// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Storage;
using Azure.Functions.Cli.Workloads.Install;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Quickstart;

internal sealed record QualifiedQuickstart(BasicAcquisitionRequest Acquisition, NativeTemplateSelection Selection);

/// <summary>
/// Joins a discovery nomination to independent release authority before any payload acquisition.
/// </summary>
internal sealed class QuickstartQualification(IBasicCompanionCatalog catalog, BasicAcquisitionPlanner planner,
    IBasicTemplateLifecycle templates, IWorkloadStore workloads, ICliVersionProvider hostVersion)
{
    private readonly IBasicCompanionCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly BasicAcquisitionPlanner _planner = planner ?? throw new ArgumentNullException(nameof(planner));
    private readonly IBasicTemplateLifecycle _templates = templates ?? throw new ArgumentNullException(nameof(templates));
    private readonly IWorkloadStore _workloads = workloads ?? throw new ArgumentNullException(nameof(workloads));
    private readonly ICliVersionProvider _hostVersion = hostVersion ?? throw new ArgumentNullException(nameof(hostVersion));

    public async Task<QualifiedQuickstart> ResolveAsync(QuickstartCatalogEntry nomination, QuickstartInstalledQuery query,
        WorkloadEntry owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nomination);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(owner);
        cancellationToken.ThrowIfCancellationRequested();
        if (NativeTemplateOwnerIdentity.FromEntry(owner) != query.Owner)
        {
            throw Unqualified("The selected live stack owner changed. Select it again.");
        }
        if (nomination.MetadataStatus != QuickstartPackageMetadataStatus.Unverified
            || nomination.PackageReference is not { Artifact: { } digest } reference
            || !string.Equals(digest.Algorithm, "sha256", StringComparison.OrdinalIgnoreCase))
        {
            throw Unqualified("This discovery entry has no usable exact package, template and SHA256 nomination. It cannot authorize acquisition.");
        }
        if (!NuGetVersion.TryParse(reference.Version, out NuGetVersion? advertisedVersion)
            || !NuGetVersion.TryParse(owner.LogicalPackage?.PackageVersion ?? owner.PackageVersion, out NuGetVersion? stackVersion)
            || !NuGetVersion.TryParse(_hostVersion.Version, out NuGetVersion? hostVersion))
        {
            throw Unqualified("An exact package, selected stack or actual CLI host version is invalid. Repair the metadata before retrying.");
        }
        BasicCompanionManifest manifest = await _catalog.LoadAsync(cancellationToken);
        if (manifest.SchemaVersion != 2 || manifest.Stacks.Count == 0)
        {
            throw Unqualified("No independently reviewed schema2 native quickstart authority is bundled. Publisher qualification is required; remote claims and installed receipts are not approval.");
        }
        IReadOnlyList<WorkloadEntry> installed = await _workloads.GetWorkloadsAsync(cancellationToken);
        if (installed.Count(entry => NativeTemplateOwnerIdentity.FromEntry(entry) == query.Owner) != 1)
        {
            throw Unqualified("The live stack owner is no longer uniquely installed. Select it again.");
        }
        string stackId = owner.LogicalPackage?.PackageId ?? owner.PackageId;
        NativeTemplateSelection selection = new(reference.TemplateIdentity,
            new(reference.PackageId, reference.Version, digest.Digest), reference.Source, query.Owner, query.Channel);
        BasicAcquisitionRequest request = new(stackId, stackVersion, query.Directory,
            reference.Source, IncludePrerelease: advertisedVersion.IsPrerelease)
        {
            NativeContext = new(owner, FuncTemplateEngineHost.Identifier, hostVersion, query.Channel, [NativeTemplateContentRole.AdditionalProject]),
            OptionalChoices = BasicAcquisitionPlanner.IsOptional(manifest, stackId, stackVersion, reference.PackageId) ? [selection] : [],
        };
        BasicAcquisitionPlan plan = _planner.Plan(manifest, request, installed, reference.Source)
            ?? throw Unqualified("No qualified native association covers the selected stack.");
        BasicTemplateArtifact[] matches = [.. plan.Companions.SelectMany(companion => companion.Candidates).Where(artifact =>
            string.Equals(artifact.PackageId, reference.PackageId, StringComparison.OrdinalIgnoreCase)
            && artifact.Version.ToNormalizedString() == reference.Version && artifact.Source == reference.Source
            && string.Equals(artifact.Sha256, digest.Digest, StringComparison.OrdinalIgnoreCase)
            && artifact.Templates.Any(template => template.Identity == reference.TemplateIdentity && template.Type == "project")
            && artifact.ContentRoles.Any(content => content.Identity == reference.TemplateIdentity && content.Role == NativeTemplateContentRole.AdditionalProject))];
        if (matches.Length != 1)
        {
            throw Unqualified("The nominated exact artifact and full project identity have no unique independently qualified additional-project association.");
        }
        BasicTemplateArtifact nominated = matches[0];
        List<BasicTemplateArtifact> artifacts = [];
        List<BasicTemplateSelection> selections = [];
        string inventory = WorkloadStateSnapshot.Capture(installed);
        TemplateEngineContext context = new(query.Directory) { RequireCleanStore = true };
        foreach (BasicCompanionPlan companion in plan.Companions)
        {
            BasicCompanionPlan choices = companion.Candidates.Contains(nominated) ? companion with { Candidates = [nominated] } : companion;
            BasicTemplateSelection selected = string.Equals(choices.PackageId, nominated.PackageId, StringComparison.OrdinalIgnoreCase)
                ? await _templates.SelectOwnedUpdateAsync(choices, nominated, selection.TemplateIdentity, query.Owner, inventory, context, cancellationToken)
                : await _templates.SelectAsync(choices, context, cancellationToken);
            artifacts.Add(selected.Artifact);
            selections.Add(selected);
        }
        BasicAcquisitionApproval approval = new(plan, artifacts.AsReadOnly())
        {
            WorkloadInventory = inventory,
            Updates = [.. selections.Where(selected => selected.Update is not null).Select(selected => selected.Update!)],
        };
        approval.Validate(plan);
        return new(request with { Approval = approval }, selection);
    }

    private static BasicAcquisitionException Unqualified(string message) => new(BasicAcquisitionFailure.MissingMapping, message);
}
