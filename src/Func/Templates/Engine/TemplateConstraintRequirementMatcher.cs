// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed class TemplateConstraintRequirementMatcher
{
    public bool MatchesWorkload(TemplateConstraintAlternative requirement, string packageId, NuGetVersion version, IReadOnlyCollection<string> aliases, string? publishedPackageId = null)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(aliases);
        if (string.IsNullOrWhiteSpace(requirement.Identity))
        {
            throw new ArgumentException("A workload requirement must have an identity.", nameof(requirement));
        }

        bool identityMatches = string.Equals(requirement.Identity, packageId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(requirement.Identity, publishedPackageId, StringComparison.OrdinalIgnoreCase)
            || aliases.Any(alias => string.Equals(requirement.Identity, alias, StringComparison.OrdinalIgnoreCase));
        return identityMatches && MatchesVersion(requirement.Version, version);
    }

    public bool MatchesBundle(TemplateConstraintAlternative requirement, string? bundleId, NuGetVersion? bundleVersion)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        return !string.IsNullOrWhiteSpace(bundleId)
            && bundleVersion is not null
            && (requirement.Identity is null || string.Equals(requirement.Identity, bundleId, StringComparison.OrdinalIgnoreCase))
            && MatchesVersion(requirement.Version, bundleVersion);
    }

    private static bool MatchesVersion(VersionRange? range, NuGetVersion version)
        => range is null || WorkloadVersionRanges.SatisfiesRange(range, version, includePrerelease: true);
}