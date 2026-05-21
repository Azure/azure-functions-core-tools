// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Versioning;

namespace Azure.Functions.Cli.Bundles;

internal static class VersionRangeIntersection
{
    public static VersionRange? Intersect(string hostJsonRange, string? profileRange)
    {
        ArgumentException.ThrowIfNullOrEmpty(hostJsonRange);

        var host = VersionRange.Parse(hostJsonRange);
        if (string.IsNullOrWhiteSpace(profileRange))
        {
            return host;
        }

        var profile = VersionRange.Parse(profileRange);
        return Intersect(host, profile);
    }

    public static NuGetVersion? FindBest(IEnumerable<NuGetVersion> candidates, VersionRange range)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(range);

        return candidates
            .Where(range.Satisfies)
            .OrderByDescending(v => v)
            .FirstOrDefault();
    }

    private static VersionRange? Intersect(VersionRange a, VersionRange b)
    {
        // NuGet.Versioning has no public Intersect; reconstruct from the tighter of each bound.
        NuGetVersion? min = HigherBound(a.MinVersion, a.IsMinInclusive, b.MinVersion, b.IsMinInclusive,
            out bool minInclusive);
        NuGetVersion? max = LowerBound(a.MaxVersion, a.IsMaxInclusive, b.MaxVersion, b.IsMaxInclusive,
            out bool maxInclusive);

        if (min is not null && max is not null)
        {
            int cmp = min.CompareTo(max);
            if (cmp > 0 || (cmp == 0 && (!minInclusive || !maxInclusive)))
            {
                return null;
            }
        }

        return new VersionRange(min, minInclusive, max, maxInclusive);
    }

    private static NuGetVersion? HigherBound(NuGetVersion? a, bool aIncl, NuGetVersion? b, bool bIncl, out bool inclusive)
    {
        if (a is null)
        {
            inclusive = bIncl;
            return b;
        }

        if (b is null)
        {
            inclusive = aIncl;
            return a;
        }

        int cmp = a.CompareTo(b);
        if (cmp > 0)
        {
            inclusive = aIncl;
            return a;
        }

        if (cmp < 0)
        {
            inclusive = bIncl;
            return b;
        }

        inclusive = aIncl && bIncl;
        return a;
    }

    private static NuGetVersion? LowerBound(NuGetVersion? a, bool aIncl, NuGetVersion? b, bool bIncl, out bool inclusive)
    {
        if (a is null)
        {
            inclusive = bIncl;
            return b;
        }

        if (b is null)
        {
            inclusive = aIncl;
            return a;
        }

        int cmp = a.CompareTo(b);
        if (cmp < 0)
        {
            inclusive = aIncl;
            return a;
        }

        if (cmp > 0)
        {
            inclusive = bIncl;
            return b;
        }

        inclusive = aIncl && bIncl;
        return a;
    }
}
