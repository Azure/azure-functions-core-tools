// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Acquisition;

internal sealed class BasicAcquisitionPlanner
{
    public BasicAcquisitionPlan? Plan(BasicCompanionManifest manifest, BasicAcquisitionRequest request,
        IReadOnlyList<WorkloadEntry> installed, string source)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StackPackageId);
        ArgumentNullException.ThrowIfNull(request.StackVersion);
        ArgumentNullException.ThrowIfNull(request.Directory);
        string effectiveSource = BasicCompanionManifestReader.Source(source);
        if (manifest.Stacks.Count == 0)
        {
            throw Missing("No publisher-qualified basic companion mappings are bundled. Continue using the current setup/init experience.");
        }

        BasicStackMapping? requested = FindMapping(manifest, request.StackPackageId, request.StackVersion);
        if (requested is null)
        {
            return null;
        }

        List<BasicStackMapping> requirements = [requested];
        foreach (WorkloadEntry entry in installed)
        {
            // Keep unselected and side-by-side rows: a failed companion install does not erase the retained stack's requirements.
            string id = entry.LogicalPackage?.PackageId ?? entry.PackageId;
            if (!manifest.Stacks.Any(mapping => string.Equals(mapping.PackageId, id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string versionText = entry.LogicalPackage?.PackageVersion ?? entry.PackageVersion;
            if (!NuGetVersion.TryParse(versionText, out NuGetVersion? version))
            {
                throw Missing($"Installed stack '{id}' has an invalid version '{versionText}'. Repair its installed state before acquisition.");
            }

            BasicStackMapping mapping = FindMapping(manifest, id, version)
                ?? throw Missing($"Installed stack '{id}' is not covered by the bundled mapping.");
            requirements.Add(mapping);
        }

        Dictionary<string, VersionRange> ranges = new(StringComparer.OrdinalIgnoreCase);
        foreach (BasicStackMapping mapping in requirements)
        {
            foreach (BasicCompanionRequirement companion in mapping.Companions)
            {
                VersionRange range = ranges.TryGetValue(companion.PackageId, out VersionRange? existing)
                    ? VersionRangeIntersection.Intersect(existing.ToNormalizedString(), companion.Versions.ToNormalizedString())
                        ?? throw new BasicAcquisitionException(BasicAcquisitionFailure.RangeConflict,
                            $"Retained stack requirements for '{companion.PackageId}' do not overlap. No packages were changed.")
                    : companion.Versions;
                ranges[companion.PackageId] = range;
            }
        }

        List<BasicCompanionPlan> companions = [];
        foreach (BasicCompanionRequirement companion in requested.Companions)
        {
            foreach (BasicStackMapping mapping in requirements.Where(mapping =>
                mapping.Companions.Any(c => string.Equals(c.PackageId, companion.PackageId, StringComparison.OrdinalIgnoreCase))))
            {
                if (!mapping.Sources.Contains(effectiveSource, StringComparer.Ordinal))
                {
                    throw new BasicAcquisitionException(BasicAcquisitionFailure.SourceConflict,
                        $"Selected source '{effectiveSource}' is not approved for stack '{mapping.PackageId}'. Allowed: {string.Join(", ", mapping.Sources)}.");
                }
            }

            BasicTemplateArtifact[] candidates = [.. manifest.Artifacts.Where(artifact =>
                string.Equals(artifact.PackageId, companion.PackageId, StringComparison.OrdinalIgnoreCase)
                && artifact.Source == effectiveSource
                && (!artifact.Version.IsPrerelease || request.IncludePrerelease)
                && ranges[companion.PackageId].Satisfies(artifact.Version))
                .OrderByDescending(artifact => artifact.Version)];
            if (candidates.Length == 0)
            {
                throw Missing($"No qualified exact artifact for '{companion.PackageId}' satisfies '{ranges[companion.PackageId]}' on '{effectiveSource}'. Request updated publisher-qualified mapping data.");
            }

            companions.Add(new(companion.PackageId, ranges[companion.PackageId], Array.AsReadOnly(candidates)));
        }

        return new(manifest.Revision, effectiveSource, companions.AsReadOnly());
    }

    private static BasicStackMapping? FindMapping(BasicCompanionManifest manifest, string id, NuGetVersion version)
    {
        BasicStackMapping[] known = [.. manifest.Stacks.Where(mapping => string.Equals(mapping.PackageId, id, StringComparison.OrdinalIgnoreCase))];
        BasicStackMapping[] matching = [.. known.Where(mapping => mapping.Versions.Satisfies(version))];
        if (known.Length > 0 && matching.Length == 0)
        {
            throw Missing($"Approved stack '{id}' {version} has no historical mapping in revision '{manifest.Revision}'. Request a CLI release retaining that stack's requirements.");
        }

        return matching.Length switch
        {
            0 => null,
            1 => matching[0],
            _ => throw new BasicAcquisitionException(BasicAcquisitionFailure.InvalidMapping, $"Stack '{id}' {version} has ambiguous mappings."),
        };
    }

    private static BasicAcquisitionException Missing(string message) => new(BasicAcquisitionFailure.MissingMapping, message);
}
