// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Versioning;

namespace Azure.Functions.Cli.Bundles.Tests;

public class VersionRangeIntersectionTests
{
    [Fact]
    public void NullProfile_ReturnsHostRangeUnchanged()
    {
        VersionRange? result = VersionRangeIntersection.Intersect("[4.0.0, 5.0.0)", null);
        result.Should().NotBeNull();
        result.Satisfies(NuGetVersion.Parse("4.22.0")).Should().BeTrue();
        result.Satisfies(NuGetVersion.Parse("5.0.0")).Should().BeFalse();
    }

    [Fact]
    public void OverlappingRanges_ReturnsTighterIntersection()
    {
        VersionRange? result = VersionRangeIntersection.Intersect("[4.0.0, 5.0.0)", "[4.10.0, 6.0.0)");
        result.Should().NotBeNull();
        result.Satisfies(NuGetVersion.Parse("4.22.0")).Should().BeTrue();
        result.Satisfies(NuGetVersion.Parse("4.5.0")).Should().BeFalse();
        result.Satisfies(NuGetVersion.Parse("5.0.0")).Should().BeFalse();
    }

    [Fact]
    public void DisjointRanges_ReturnsNull()
    {
        VersionRangeIntersection.Intersect("[4.0.0, 5.0.0)", "[5.0.0, 6.0.0)").Should().BeNull();
    }

    [Fact]
    public void NestedRanges_ReturnsInnerRange()
    {
        VersionRange? result = VersionRangeIntersection.Intersect("[1.0.0, 9.0.0)", "[4.0.0, 5.0.0)");
        result.Should().NotBeNull();
        result.Satisfies(NuGetVersion.Parse("4.5.0")).Should().BeTrue();
        result.Satisfies(NuGetVersion.Parse("5.0.0")).Should().BeFalse();
        result.Satisfies(NuGetVersion.Parse("3.9.0")).Should().BeFalse();
    }

    [Fact]
    public void FindBest_ReturnsHighestSatisfying()
    {
        NuGetVersion[] candidates =
        [
            NuGetVersion.Parse("4.10.0"),
            NuGetVersion.Parse("4.22.0"),
            NuGetVersion.Parse("4.5.0"),
            NuGetVersion.Parse("5.1.0"),
        ];

        NuGetVersion? best = VersionRangeIntersection.FindBest(candidates, VersionRange.Parse("[4.0.0, 5.0.0)"));
        best.Should().Be(NuGetVersion.Parse("4.22.0"));
    }

    [Fact]
    public void FindBest_NoneMatching_ReturnsNull()
    {
        NuGetVersion[] candidates = [NuGetVersion.Parse("3.0.0"), NuGetVersion.Parse("5.0.0")];
        VersionRangeIntersection.FindBest(candidates, VersionRange.Parse("[4.0.0, 5.0.0)")).Should().BeNull();
    }
}
