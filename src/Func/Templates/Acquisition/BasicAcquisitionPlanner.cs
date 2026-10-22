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
        if (manifest.SchemaVersion is not (1 or 2))
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.InvalidMapping, "The acquisition authority uses an unsupported schema.");
        }
        NativeTemplateAssociationContext? native = request.NativeContext;
        if (request.OptionalChoices.Count > 0 && native is null)
        {
            throw Missing("Optional native choices require the actual selected stack context.");
        }
        if ((manifest.SchemaVersion == 2) != (native is not null))
        {
            throw Missing("Required external native content needs an explicit qualified native association and selected stack context. Legacy basic mappings cannot satisfy it.");
        }
        if (native is not null)
        {
            ArgumentNullException.ThrowIfNull(native.SelectedStack);
            ArgumentNullException.ThrowIfNull(native.EngineHostVersion);
            ArgumentNullException.ThrowIfNull(native.RequiredRoles);
            if (native.RequiredRoles.Count == 0 || native.RequiredRoles.Any(role => !Enum.IsDefined(role)))
            {
                throw new ArgumentException("A native request requires recognized external content roles.", nameof(request));
            }
            string selectedId = native.SelectedStack.LogicalPackage?.PackageId ?? native.SelectedStack.PackageId;
            string selectedVersion = native.SelectedStack.LogicalPackage?.PackageVersion ?? native.SelectedStack.PackageVersion;
            if (!string.Equals(selectedId, request.StackPackageId, StringComparison.OrdinalIgnoreCase)
                || !NuGetVersion.TryParse(selectedVersion, out NuGetVersion? version) || version != request.StackVersion)
            {
                throw new BasicAcquisitionException(BasicAcquisitionFailure.PlanChanged,
                    "The requested native association does not match the selected stack identity and version. Select the stack again.");
            }
        }
        string effectiveSource = BasicCompanionManifestReader.Source(source);
        if (native is not null)
        {
            ValidateNativeOrigin(native.SelectedStack, effectiveSource);
        }
        if (manifest.Stacks.Count == 0)
        {
            throw Missing("No publisher-qualified basic companion mappings are bundled. Continue using the current setup/init experience.");
        }

        BasicStackMapping? requested = FindMapping(manifest, request.StackPackageId, request.StackVersion);
        if (requested is null)
        {
            if (native is not null)
            {
                throw Missing($"No publisher-qualified native association covers selected stack '{request.StackPackageId}' {request.StackVersion}. Bundled basic readiness does not satisfy required external templates.");
            }
            return null;
        }
        Dictionary<string, NativeTemplateSelection> choices = new(StringComparer.OrdinalIgnoreCase);
        foreach (NativeTemplateSelection choice in request.OptionalChoices)
        {
            if (choice is null || choice.Artifact is null || choice.Owner != NativeTemplateOwnerIdentity.FromEntry(native!.SelectedStack)
                || choice.Channel != native.Channel || choice.Source != effectiveSource
                || !choices.TryAdd(choice.Artifact.PackageId, choice)
                || !requested.Companions.Any(companion => companion.Kind == BasicCompanionKind.Optional
                    && string.Equals(companion.PackageId, choice.Artifact.PackageId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new BasicAcquisitionException(BasicAcquisitionFailure.PlanChanged,
                    "An optional choice is duplicate, foreign or absent from the qualified selected-stack mapping. Select it again.");
            }
        }
        requested = requested with
        {
            Companions = Array.AsReadOnly<BasicCompanionRequirement>([.. requested.Companions.Where(companion =>
                companion.Kind == BasicCompanionKind.Required || choices.ContainsKey(companion.PackageId))]),
        };
        if (native is not null)
        {
            ValidateNativePolicy(requested, native);
            if (native.RequiredRoles.Any(role => !requested.Companions.Any(companion => companion.RequiredRoles.Contains(role))))
            {
                throw Missing("The qualified association does not supply every required external content role. Bundled basic templates cannot discharge that dependency.");
            }
        }

        List<BasicStackMapping> requirements = [requested];
        List<(BasicStackMapping Mapping, WorkloadEntry Entry)> retained = [];
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
            mapping = mapping with
            {
                Companions = Array.AsReadOnly<BasicCompanionRequirement>([.. mapping.Companions.Where(companion =>
                    companion.Kind == BasicCompanionKind.Required)]),
            };
            requirements.Add(mapping);
            retained.Add((mapping, entry));
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
            choices.TryGetValue(companion.PackageId, out NativeTemplateSelection? choice);
            BasicStackMapping[] contributors = [.. requirements.Where(mapping =>
                mapping.Companions.Any(c => string.Equals(c.PackageId, companion.PackageId, StringComparison.OrdinalIgnoreCase)))];
            foreach (BasicStackMapping mapping in contributors)
            {
                if (!mapping.Sources.Contains(effectiveSource, StringComparer.Ordinal))
                {
                    throw new BasicAcquisitionException(BasicAcquisitionFailure.SourceConflict,
                        $"Selected source '{effectiveSource}' is not approved for stack '{mapping.PackageId}'. Allowed: {string.Join(", ", mapping.Sources)}.");
                }
                if (native is not null)
                {
                    ValidateNativePolicy(mapping, native);
                    foreach ((_, WorkloadEntry entry) in retained.Where(requirement => ReferenceEquals(requirement.Mapping, mapping)))
                    {
                        ValidateNativeOrigin(entry, effectiveSource);
                    }
                }
            }

            NativeTemplateContentRole[] roles = [.. contributors
                .SelectMany(mapping => mapping.Companions)
                .Where(requirement => string.Equals(requirement.PackageId, companion.PackageId, StringComparison.OrdinalIgnoreCase))
                .SelectMany(requirement => requirement.RequiredRoles)
                .Distinct()];
            BasicTemplateArtifact[] candidates = [.. manifest.Artifacts.Where(artifact =>
                string.Equals(artifact.PackageId, companion.PackageId, StringComparison.OrdinalIgnoreCase)
                && artifact.Source == effectiveSource
                && (choice is null || (artifact.Version.ToNormalizedString() == choice.Artifact.Version
                    && string.Equals(artifact.Sha256, choice.Artifact.Sha256, StringComparison.OrdinalIgnoreCase)
                    && artifact.Templates.Any(template => template.Identity == choice.TemplateIdentity && template.Type == "project")
                    && artifact.ContentRoles.Any(content => content.Identity == choice.TemplateIdentity
                        && content.Role == NativeTemplateContentRole.AdditionalProject)))
                && (!artifact.Version.IsPrerelease || request.IncludePrerelease)
                && (native is null || ((native.Channel is null || BundleHelpers.MatchesChannel(artifact.Version, native.Channel.Value))
                    && contributors.All(mapping => mapping.NativePolicy!.Channels.Any(channel => BundleHelpers.MatchesChannel(artifact.Version, channel)))
                    && roles.All(role => artifact.ContentRoles.Any(content => content.Role == role))))
                && ranges[companion.PackageId].Satisfies(artifact.Version))
                .OrderByDescending(artifact => artifact.Version)];
            if (candidates.Length == 0)
            {
                throw Missing($"No qualified exact artifact for '{companion.PackageId}' satisfies '{ranges[companion.PackageId]}' on '{effectiveSource}'. Request updated publisher-qualified mapping data.");
            }

            companions.Add(new(companion.PackageId, ranges[companion.PackageId], Array.AsReadOnly(candidates)));
        }

        return new(manifest.Revision, effectiveSource, companions.AsReadOnly())
        {
            OptionalChoices = request.OptionalChoices,
            Owner = native is null ? null : NativeTemplateOwnerIdentity.FromEntry(native.SelectedStack),
            HostIdentifier = native?.HostIdentifier,
            EngineHostVersion = native?.EngineHostVersion,
            Channel = native?.Channel,
            RequiredRoles = native?.RequiredRoles ?? [],
        };
    }

    internal static bool IsOptional(BasicCompanionManifest manifest, string stackId, NuGetVersion stackVersion, string packageId)
        => FindMapping(manifest, stackId, stackVersion)?.Companions.Any(companion =>
            companion.Kind == BasicCompanionKind.Optional && string.Equals(companion.PackageId, packageId, StringComparison.OrdinalIgnoreCase)) == true;

    private static void ValidateNativeOrigin(WorkloadEntry entry, string effectiveSource)
    {
        string installedSource = entry.LogicalPackage?.Source ?? entry.Source;
        string origin;
        try
        {
            origin = BasicCompanionManifestReader.Source(installedSource);
        }
        catch (BasicAcquisitionException exception)
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.SourceConflict,
                $"Stack '{entry.PackageId}' has no qualified catalog source binding. Local paths and unknown origins are not template feed authority.", exception);
        }
        if (origin != effectiveSource)
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.SourceConflict,
                $"The native source conflicts with retained stack '{entry.PackageId}' catalog origin. No source switch or fallback is authorized.");
        }
    }

    private static void ValidateNativePolicy(BasicStackMapping mapping, NativeTemplateAssociationContext context)
    {
        NativeTemplateAssociationPolicy policy = mapping.NativePolicy
            ?? throw Missing($"Stack '{mapping.PackageId}' has no qualified native host and channel association.");
        if (policy.EngineHostVersions is null || policy.Channels is null || policy.Channels.Count == 0)
        {
            throw Missing($"Stack '{mapping.PackageId}' has incomplete native host or channel qualification.");
        }
        if (policy.HostIdentifier != "func" || context.HostIdentifier != policy.HostIdentifier
            || !policy.EngineHostVersions.Satisfies(context.EngineHostVersion))
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.HostConflict,
                $"The native association for '{mapping.PackageId}' does not support the actual FUNC host '{context.HostIdentifier}' {context.EngineHostVersion}.");
        }
        if (context.Channel is { } channel && !policy.Channels.Contains(channel))
        {
            throw new BasicAcquisitionException(BasicAcquisitionFailure.ChannelConflict,
                $"The selected channel is not qualified for stack '{mapping.PackageId}'. No alternate channel was selected.");
        }
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
