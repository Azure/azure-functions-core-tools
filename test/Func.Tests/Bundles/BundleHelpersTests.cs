// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using NuGet.Versioning;

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
        BundleHelpers.GetBundleChannel(v).Should().Be(BundleChannel.Stable);
    }

    [Theory]
    [InlineData("4.17.0-preview.1")]
    [InlineData("4.17.0-preview.1.ci.11111.0")]
    [InlineData("5.0.0-preview")]
    [InlineData("5.0.0-Preview.2")]
    public void GetBundleChannel_PreviewLabel_ReturnsPreview(string version)
    {
        var v = NuGetVersion.Parse(version);
        BundleHelpers.GetBundleChannel(v).Should().Be(BundleChannel.Preview);
    }

    [Theory]
    [InlineData("4.17.0-experimental.1")]
    [InlineData("4.17.0-experimental.1.ci.11111.0")]
    [InlineData("5.0.0-experimental")]
    [InlineData("5.0.0-Experimental.3")]
    public void GetBundleChannel_ExperimentalLabel_ReturnsExperimental(string version)
    {
        var v = NuGetVersion.Parse(version);
        BundleHelpers.GetBundleChannel(v).Should().Be(BundleChannel.Experimental);
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
        BundleHelpers.GetBundleChannel(v).Should().Be(BundleChannel.Stable);
    }

    [Fact]
    public void GetBundleChannel_PreviewBeforeExperimental_ReturnsPreview()
    {
        // Only the first release label is checked; "preview" is first here.
        var v = NuGetVersion.Parse("5.0.0-preview.experimental.1");
        BundleHelpers.GetBundleChannel(v).Should().Be(BundleChannel.Preview);
    }

    [Fact]
    public void GetBundleChannel_ExperimentalBeforePreview_ReturnsExperimental()
    {
        // Only the first release label is checked; "experimental" is first here.
        var v = NuGetVersion.Parse("5.0.0-experimental.preview.1");
        BundleHelpers.GetBundleChannel(v).Should().Be(BundleChannel.Experimental);
    }

    // --- GetBundleChannel(string bundleId) ---

    [Fact]
    public void GetBundleChannelByBundleId_StableId_ReturnsStable()
    {
        BundleHelpers.GetBundleChannel(BundleHelpers.StableBundleId)
            .Should().Be(BundleChannel.Stable);
    }

    [Fact]
    public void GetBundleChannelByBundleId_PreviewId_ReturnsPreview()
    {
        BundleHelpers.GetBundleChannel(BundleHelpers.PreviewBundleId)
            .Should().Be(BundleChannel.Preview);
    }

    [Fact]
    public void GetBundleChannelByBundleId_ExperimentalId_ReturnsExperimental()
    {
        BundleHelpers.GetBundleChannel(BundleHelpers.ExperimentalBundleId)
            .Should().Be(BundleChannel.Experimental);
    }

    [Theory]
    [InlineData("microsoft.azure.functions.extensionbundle", BundleChannel.Stable)]
    [InlineData("MICROSOFT.AZURE.FUNCTIONS.EXTENSIONBUNDLE.PREVIEW", BundleChannel.Preview)]
    [InlineData("Microsoft.Azure.Functions.ExtensionBundle.EXPERIMENTAL", BundleChannel.Experimental)]
    public void GetBundleChannelByBundleId_CaseInsensitive(string bundleId, BundleChannel expectedChannel)
    {
        // Should not throw — case-insensitive matching.
        var channel = BundleHelpers.GetBundleChannel(bundleId);
        channel.Should().Be(expectedChannel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("SomeOther.Bundle")]
    [InlineData("Microsoft.Azure.Functions.ExtensionBundle.Unknown")]
    public void GetBundleChannelByBundleId_InvalidId_Throws(string bundleId)
    {
        FluentActions.Invoking(() => BundleHelpers.GetBundleChannel(bundleId))
            .Should().ThrowExactly<ArgumentException>();
    }

    // --- TryGetBundleChannel ---

    [Theory]
    [InlineData("Microsoft.Azure.Functions.ExtensionBundle", BundleChannel.Stable)]
    [InlineData("Microsoft.Azure.Functions.ExtensionBundle.Preview", BundleChannel.Preview)]
    [InlineData("Microsoft.Azure.Functions.ExtensionBundle.Experimental", BundleChannel.Experimental)]
    public void TryGetBundleChannel_KnownId_ReturnsTrueWithChannel(string bundleId, BundleChannel expected)
    {
        var result = BundleHelpers.TryGetBundleChannel(bundleId, out var channel);
        result.Should().BeTrue();
        channel.Should().Be(expected);
    }

    [Theory]
    [InlineData("microsoft.azure.functions.extensionbundle")]
    [InlineData("MICROSOFT.AZURE.FUNCTIONS.EXTENSIONBUNDLE.PREVIEW")]
    public void TryGetBundleChannel_CaseInsensitive_ReturnsTrue(string bundleId)
    {
        BundleHelpers.TryGetBundleChannel(bundleId, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unknown.Bundle")]
    public void TryGetBundleChannel_UnknownId_ReturnsFalseWithUnknown(string bundleId)
    {
        var result = BundleHelpers.TryGetBundleChannel(bundleId, out var channel);
        result.Should().BeFalse();
        channel.Should().Be(BundleChannel.Unknown);
    }

    // --- ToDisplayString ---

    [Theory]
    [InlineData(BundleChannel.Unknown, "unknown")]
    [InlineData(BundleChannel.Stable, "stable")]
    [InlineData(BundleChannel.Preview, "preview")]
    [InlineData(BundleChannel.Experimental, "experimental")]
    public void ToDisplayString_ReturnsExpected(BundleChannel channel, string expected)
    {
        channel.ToDisplayString().Should().Be(expected);
    }

    [Fact]
    public void ToDisplayString_InvalidChannel_Throws()
    {
        FluentActions.Invoking(() => ((BundleChannel)99).ToDisplayString())
            .Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    // --- ToPrereleaseLabel ---

    [Fact]
    public void ToPrereleaseLabel_Stable_ReturnsNull()
    {
        BundleChannel.Stable.ToPrereleaseLabel().Should().BeNull();
    }

    [Theory]
    [InlineData(BundleChannel.Preview, "preview")]
    [InlineData(BundleChannel.Experimental, "experimental")]
    public void ToPrereleaseLabel_PrereleaseChannel_ReturnsLabel(BundleChannel channel, string expected)
    {
        channel.ToPrereleaseLabel().Should().Be(expected);
    }

    [Theory]
    [InlineData(BundleChannel.Unknown)]
    [InlineData((BundleChannel)99)]
    public void ToPrereleaseLabel_InvalidChannel_Throws(BundleChannel channel)
    {
        FluentActions.Invoking(() => channel.ToPrereleaseLabel())
            .Should().ThrowExactly<ArgumentOutOfRangeException>();
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
        BundleHelpers.MatchesChannel(NuGetVersion.Parse(version), channel).Should().Be(expected);
    }

    [Fact]
    public void MatchesChannel_NullVersion_Throws()
    {
        FluentActions.Invoking(() => BundleHelpers.MatchesChannel(null!, BundleChannel.Stable))
            .Should().ThrowExactly<ArgumentNullException>();
    }

    // --- GetBundleChannel(NuGetVersion) null guard ---

    [Fact]
    public void GetBundleChannelByVersion_Null_Throws()
    {
        FluentActions.Invoking(() => BundleHelpers.GetBundleChannel((NuGetVersion)null!))
            .Should().ThrowExactly<ArgumentNullException>();
    }
}
