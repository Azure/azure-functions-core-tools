// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Acquisition;

/// <summary>
/// Binds consent to the complete resolved plan and each selected archive, including its inventory and content revision.
/// </summary>
internal sealed record BasicAcquisitionApproval(BasicAcquisitionPlan Plan, IReadOnlyList<BasicTemplateArtifact> Artifacts)
{
    public BasicAcquisitionPlan Plan { get; } = Plan ?? throw new ArgumentNullException(nameof(Plan));

    public IReadOnlyList<BasicTemplateArtifact> Artifacts { get; } = BasicCompanionPlan.Freeze(
        Artifacts ?? throw new ArgumentNullException(nameof(Artifacts)));

    public IReadOnlyList<BasicOwnedTemplateUpdate> Updates { get; init => field = Array.AsReadOnly(
        (value ?? throw new ArgumentNullException(nameof(value))).ToArray()); } = [];

    public string? WorkloadInventory { get; init; }

    public void Validate(BasicAcquisitionPlan current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (Plan.Revision != current.Revision || Plan.Source != current.Source
            || Plan.Owner != current.Owner || Plan.HostIdentifier != current.HostIdentifier
            || Plan.EngineHostVersion != current.EngineHostVersion || Plan.Channel != current.Channel
            || !Plan.RequiredRoles.SequenceEqual(current.RequiredRoles)
            || !Plan.OptionalChoices.SequenceEqual(current.OptionalChoices)
            || Plan.Companions.Count != current.Companions.Count
            || !Plan.Companions.Zip(current.Companions).All(pair =>
                pair.First.PackageId == pair.Second.PackageId
                && pair.First.Versions.Equals(pair.Second.Versions)
                && pair.First.Candidates.Count == pair.Second.Candidates.Count
                && pair.First.Candidates.Zip(pair.Second.Candidates).All(candidate => Same(candidate.First, candidate.Second)))
            || Artifacts.Count != current.Companions.Count
            || current.Companions.Any(companion => Artifacts.Count(artifact => companion.Candidates.Any(candidate => Same(candidate, artifact))) != 1)
            || Updates.Select(update => update.After.PackageId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Updates.Count
            || Updates.Any(update => !Artifacts.Any(artifact => Same(update.After, artifact))
                || Plan.Owner is null || Plan.Owner != update.Before.Owner
                || WorkloadInventory is null || WorkloadInventory != update.Before.WorkloadInventory
                || string.IsNullOrWhiteSpace(update.Before.SelectedProjectIdentity)
                || !update.After.ContentRoles.Any(role => role.Identity == update.Before.SelectedProjectIdentity
                    && role.Role == NativeTemplateContentRole.AdditionalProject)
                || !NuGet.Versioning.NuGetVersion.TryParse(update.Before.Archive.Version, out NuGet.Versioning.NuGetVersion? beforeVersion)
                || NuGet.Versioning.VersionComparer.VersionRelease.Compare(update.After.Version, beforeVersion) <= 0))
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.PlanChanged,
                "The qualified acquisition plan changed after consent. Select and approve it again; no alternate artifact is authorized.");
        }
    }

    public BasicCompanionPlan Bind(BasicCompanionPlan companion)
    {
        ArgumentNullException.ThrowIfNull(companion);
        BasicTemplateArtifact artifact = Artifacts.Single(artifact => companion.Candidates.Any(candidate => Same(candidate, artifact)));
        return companion with { Candidates = [artifact] };
    }

    public BasicOwnedTemplateUpdate? UpdateFor(string packageId)
        => Updates.SingleOrDefault(update => string.Equals(update.After.PackageId, packageId, StringComparison.OrdinalIgnoreCase));

    internal static bool Same(BasicTemplateArtifact left, BasicTemplateArtifact right)
        => left.PackageId == right.PackageId && left.Version == right.Version && left.Source == right.Source
            && left.Sha256 == right.Sha256 && left.ContentRevision == right.ContentRevision
            && left.Templates.SequenceEqual(right.Templates) && left.ContentRoles.SequenceEqual(right.ContentRoles);
}
