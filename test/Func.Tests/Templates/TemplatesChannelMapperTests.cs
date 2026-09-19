// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;

namespace Azure.Functions.Cli.Tests.Templates;

public class TemplatesChannelMapperTests
{
    [Fact]
    public void PickChannelMatched_Returns_Highest_Of_Matching_Channel()
    {
        IReadOnlyList<InstalledTemplatesWorkload> rows =
        [
            new("node", "1.0.0", "/n/1.0.0"),
            new("node", "1.1.0", "/n/1.1.0"),
            new("node", "1.0.0-preview", "/n/1.0.0-preview"),
            new("node", "1.1.0-preview", "/n/1.1.0-preview"),
        ];

        InstalledTemplatesWorkload? stable = TemplatesChannelMapper.PickChannelMatched(rows, BundleChannel.Stable);
        stable.Should().NotBeNull();
        stable.PackageVersion.Should().Be("1.1.0");

        InstalledTemplatesWorkload? preview = TemplatesChannelMapper.PickChannelMatched(rows, BundleChannel.Preview);
        preview.Should().NotBeNull();
        preview.PackageVersion.Should().Be("1.1.0-preview");
    }

    [Fact]
    public void PickChannelMatched_No_Match_Returns_Null()
    {
        IReadOnlyList<InstalledTemplatesWorkload> rows =
        [
            new("node", "1.0.0", "/n/1.0.0"),
        ];

        TemplatesChannelMapper.PickChannelMatched(rows, BundleChannel.Experimental).Should().BeNull();
    }

    [Theory]
    [InlineData("9.0.0-beta.1", false)]
    [InlineData("9.0.0-beta.1", true)]
    [InlineData("9.0.0-rc.1", false)]
    [InlineData("9.0.0-rc.1", true)]
    [InlineData("invalid-version", false)]
    [InlineData("invalid-version", true)]
    [InlineData("", false)]
    [InlineData("", true)]
    public void PickChannelMatched_UnfilteredRows_IgnoreInvalidOrNonStableVersions(string version, bool reverse)
    {
        InstalledTemplatesWorkload stable = new("node", "1.10.0", "stable");
        InstalledTemplatesWorkload[] rows = [new("node", version, "ineligible"), new("node", "1.9.0", "older"), stable];

        TemplatesChannelMapper.PickChannelMatched(reverse ? [.. rows.Reverse()] : rows, BundleChannel.Stable)
            .Should().BeSameAs(stable);
        TemplatesChannelMapper.PickChannelMatched([rows[0]], BundleChannel.Stable).Should().BeNull();
    }
}
