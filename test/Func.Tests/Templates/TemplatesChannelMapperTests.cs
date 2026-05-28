// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates;
using Xunit;

namespace Azure.Functions.Cli.Tests.Templates;

public class TemplatesChannelMapperTests
{
    [Theory]
    [InlineData("Microsoft.Azure.Functions.ExtensionBundle", "")]
    [InlineData("Microsoft.Azure.Functions.ExtensionBundle.Preview", "preview")]
    [InlineData("Microsoft.Azure.Functions.ExtensionBundle.Experimental", "experimental")]
    [InlineData("microsoft.azure.functions.extensionbundle", "")]
    public void GetChannelLabel_Maps_Recognised_Ids(string bundleId, string expected)
    {
        Assert.Equal(expected, TemplatesChannelMapper.GetChannelLabel(bundleId));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Some.Other.Bundle")]
    public void GetChannelLabel_Unrecognised_Returns_Null(string? bundleId)
    {
        Assert.Null(TemplatesChannelMapper.GetChannelLabel(bundleId));
    }

    [Theory]
    [InlineData("1.0.0", "")]
    [InlineData("1.0.0-preview", "preview")]
    [InlineData("1.0.0-experimental", "experimental")]
    [InlineData("1.0.0-PREVIEW", "preview")]
    [InlineData("1.0.0-preview+meta", "preview")]
    public void GetPrereleaseLabel_Extracts_Label(string version, string expected)
    {
        Assert.Equal(expected, TemplatesChannelMapper.GetPrereleaseLabel(version));
    }

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

        InstalledTemplatesWorkload? stable = TemplatesChannelMapper.PickChannelMatched(rows, "");
        Assert.NotNull(stable);
        Assert.Equal("1.1.0", stable.PackageVersion);

        InstalledTemplatesWorkload? preview = TemplatesChannelMapper.PickChannelMatched(rows, "preview");
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

        Assert.Null(TemplatesChannelMapper.PickChannelMatched(rows, "experimental"));
    }
}
