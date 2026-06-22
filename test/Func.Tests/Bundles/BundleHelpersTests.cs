// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using NuGet.Versioning;
using Xunit;

namespace Azure.Functions.Cli.Bundles.Tests;

public class BundleHelpersTests
{
    [Theory]
    [InlineData("1.0.0")]
    [InlineData("4.17.0")]
    [InlineData("5.0.0")]
    public void GetBundleChannel_StableVersion_ReturnsStable(string version)
    {
        var v = NuGetVersion.Parse(version);
        Assert.Equal(BundleChannel.Stable, BundleHelpers.GetBundleChannel(v));
    }

    [Theory]
    [InlineData("4.17.0-preview.1")]
    [InlineData("4.17.0-preview.1.ci.11111.0")]
    [InlineData("5.0.0-preview")]
    [InlineData("5.0.0-Preview.2")]
    public void GetBundleChannel_PreviewLabel_ReturnsPreview(string version)
    {
        var v = NuGetVersion.Parse(version);
        Assert.Equal(BundleChannel.Preview, BundleHelpers.GetBundleChannel(v));
    }

    [Theory]
    [InlineData("4.17.0-experimental.1")]
    [InlineData("4.17.0-experimental.1.ci.11111.0")]
    [InlineData("5.0.0-experimental")]
    [InlineData("5.0.0-Experimental.3")]
    public void GetBundleChannel_ExperimentalLabel_ReturnsExperimental(string version)
    {
        var v = NuGetVersion.Parse(version);
        Assert.Equal(BundleChannel.Experimental, BundleHelpers.GetBundleChannel(v));
    }

    [Theory]
    [InlineData("5.0.0-rc.1")]
    [InlineData("4.17.0-beta.2")]
    [InlineData("5.0.0-alpha")]
    [InlineData("5.0.0-ci.11111.0")]
    [InlineData("5.0.0-dev.11111.0")]
    public void GetBundleChannel_UnknownPrereleaseLabel_ReturnsStable(string version)
    {
        var v = NuGetVersion.Parse(version);
        Assert.Equal(BundleChannel.Stable, BundleHelpers.GetBundleChannel(v));
    }

    [Fact]
    public void GetBundleChannel_PreviewBeforeExperimental_ReturnsPreview()
    {
        // Only the first release label is checked; "preview" is first here.
        var v = NuGetVersion.Parse("5.0.0-preview.experimental.1");
        Assert.Equal(BundleChannel.Preview, BundleHelpers.GetBundleChannel(v));
    }

    [Fact]
    public void GetBundleChannel_ExperimentalBeforePreview_ReturnsExperimental()
    {
        // Only the first release label is checked; "experimental" is first here.
        var v = NuGetVersion.Parse("5.0.0-experimental.preview.1");
        Assert.Equal(BundleChannel.Experimental, BundleHelpers.GetBundleChannel(v));
    }

    // --- GetBundleChannel(string bundleId) ---

    [Fact]
    public void GetBundleChannelByBundleId_StableId_ReturnsStable()
    {
        Assert.Equal(BundleChannel.Stable, BundleHelpers.GetBundleChannel(BundleHelpers.StableBundleId));
    }

    [Fact]
    public void GetBundleChannelByBundleId_PreviewId_ReturnsPreview()
    {
        Assert.Equal(BundleChannel.Preview, BundleHelpers.GetBundleChannel(BundleHelpers.PreviewBundleId));
    }

    [Fact]
    public void GetBundleChannelByBundleId_ExperimentalId_ReturnsExperimental()
    {
        Assert.Equal(BundleChannel.Experimental, BundleHelpers.GetBundleChannel(BundleHelpers.ExperimentalBundleId));
    }

    [Theory]
    [InlineData("microsoft.azure.functions.extensionbundle", BundleChannel.Stable)]
    [InlineData("MICROSOFT.AZURE.FUNCTIONS.EXTENSIONBUNDLE.PREVIEW", BundleChannel.Preview)]
    [InlineData("Microsoft.Azure.Functions.ExtensionBundle.EXPERIMENTAL", BundleChannel.Experimental)]
    public void GetBundleChannelByBundleId_CaseInsensitive(string bundleId, BundleChannel expectedChannel)
    {
        // Should not throw — case-insensitive matching.
        var channel = BundleHelpers.GetBundleChannel(bundleId);
        Assert.Equal(expectedChannel, channel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("SomeOther.Bundle")]
    [InlineData("Microsoft.Azure.Functions.ExtensionBundle.Unknown")]
    public void GetBundleChannelByBundleId_InvalidId_Throws(string bundleId)
    {
        Assert.Throws<ArgumentException>(() => BundleHelpers.GetBundleChannel(bundleId));
    }

    // --- TryGetBundleChannel ---

    [Theory]
    [InlineData("Microsoft.Azure.Functions.ExtensionBundle", BundleChannel.Stable)]
    [InlineData("Microsoft.Azure.Functions.ExtensionBundle.Preview", BundleChannel.Preview)]
    [InlineData("Microsoft.Azure.Functions.ExtensionBundle.Experimental", BundleChannel.Experimental)]
    public void TryGetBundleChannel_KnownId_ReturnsTrueWithChannel(string bundleId, BundleChannel expected)
    {
        var result = BundleHelpers.TryGetBundleChannel(bundleId, out var channel);
        Assert.True(result);
        Assert.Equal(expected, channel);
    }

    [Theory]
    [InlineData("microsoft.azure.functions.extensionbundle")]
    [InlineData("MICROSOFT.AZURE.FUNCTIONS.EXTENSIONBUNDLE.PREVIEW")]
    public void TryGetBundleChannel_CaseInsensitive_ReturnsTrue(string bundleId)
    {
        Assert.True(BundleHelpers.TryGetBundleChannel(bundleId, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unknown.Bundle")]
    public void TryGetBundleChannel_UnknownId_ReturnsFalseWithUnknown(string bundleId)
    {
        var result = BundleHelpers.TryGetBundleChannel(bundleId, out var channel);
        Assert.False(result);
        Assert.Equal(BundleChannel.Unknown, channel);
    }

    // --- ToDisplayString ---

    [Theory]
    [InlineData(BundleChannel.Unknown, "unknown")]
    [InlineData(BundleChannel.Stable, "stable")]
    [InlineData(BundleChannel.Preview, "preview")]
    [InlineData(BundleChannel.Experimental, "experimental")]
    public void ToDisplayString_ReturnsExpected(BundleChannel channel, string expected)
    {
        Assert.Equal(expected, channel.ToDisplayString());
    }

    [Fact]
    public void ToDisplayString_InvalidChannel_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((BundleChannel)99).ToDisplayString());
    }

    // --- ToPrereleaseLabel ---

    [Fact]
    public void ToPrereleaseLabel_Stable_ReturnsNull()
    {
        Assert.Null(BundleChannel.Stable.ToPrereleaseLabel());
    }

    [Theory]
    [InlineData(BundleChannel.Preview, "preview")]
    [InlineData(BundleChannel.Experimental, "experimental")]
    public void ToPrereleaseLabel_PrereleaseChannel_ReturnsLabel(BundleChannel channel, string expected)
    {
        Assert.Equal(expected, channel.ToPrereleaseLabel());
    }

    [Theory]
    [InlineData(BundleChannel.Unknown)]
    [InlineData((BundleChannel)99)]
    public void ToPrereleaseLabel_InvalidChannel_Throws(BundleChannel channel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => channel.ToPrereleaseLabel());
    }

    // --- MatchesChannel ---

    [Theory]
    [InlineData("1.5.0", BundleChannel.Stable, true)]
    [InlineData("2.0.0-preview.1", BundleChannel.Stable, false)]
    [InlineData("2.0.0-preview.1", BundleChannel.Preview, true)]
    [InlineData("2.0.0-experimental.1", BundleChannel.Preview, false)]
    [InlineData("2.0.0-experimental.2", BundleChannel.Experimental, true)]
    [InlineData("1.5.0", BundleChannel.Preview, false)]
    public void MatchesChannel_ReturnsExpected(string version, BundleChannel channel, bool expected)
    {
        Assert.Equal(expected, BundleHelpers.MatchesChannel(NuGetVersion.Parse(version), channel));
    }

    [Fact]
    public void MatchesChannel_NullVersion_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BundleHelpers.MatchesChannel(null!, BundleChannel.Stable));
    }

    // --- GetBundleChannel(NuGetVersion) null guard ---

    [Fact]
    public void GetBundleChannelByVersion_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BundleHelpers.GetBundleChannel((NuGetVersion)null!));
    }
}
