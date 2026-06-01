// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Xunit;

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
        Assert.NotNull(stable);
        Assert.Equal("1.1.0", stable.PackageVersion);

        InstalledTemplatesWorkload? preview = TemplatesChannelMapper.PickChannelMatched(rows, BundleChannel.Preview);
        Assert.NotNull(preview);
        Assert.Equal("1.1.0-preview", preview.PackageVersion);
    }

    [Fact]
    public void PickChannelMatched_No_Match_Returns_Null()
    {
        IReadOnlyList<InstalledTemplatesWorkload> rows =
        [
            new("node", "1.0.0", "/n/1.0.0"),
        ];

        Assert.Null(TemplatesChannelMapper.PickChannelMatched(rows, BundleChannel.Experimental));
    }
}
