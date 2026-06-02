// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Helpers for evaluating <see cref="VersionRange"/> against <see cref="NuGetVersion"/>
/// candidates in a way that accounts for NuGet's prerelease handling.
/// </summary>
internal static class WorkloadVersionRanges
{
    /// <summary>
    /// Returns whether <paramref name="candidate"/> satisfies <paramref name="range"/>,
    /// honoring <paramref name="includePrerelease"/>.
    /// </summary>
    /// <remarks>
    /// NuGet's <see cref="VersionRange.Satisfies(NuGetVersion)"/> rejects prerelease
    /// candidates when the range itself has no prerelease in its bounds (for example,
    /// a range pinned to <c>[3.13.0]</c> rejects <c>3.13.0-preview.1</c> even though
    /// both share the same numeric version). When prerelease is enabled, re-check the
    /// candidate's numeric portion against the range so prerelease versions whose
    /// numeric version is inside the bounds are accepted.
    /// </remarks>
    public static bool SatisfiesRange(VersionRange range, NuGetVersion candidate, bool includePrerelease)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(candidate);

        if (range.Satisfies(candidate))
        {
            return true;
        }

        if (!includePrerelease || !candidate.IsPrerelease)
        {
            return false;
        }

        var numeric = new NuGetVersion(candidate.Major, candidate.Minor, candidate.Patch, candidate.Revision);
        return range.Satisfies(numeric);
    }
}
