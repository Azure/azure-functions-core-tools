// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class TemplatesWorkloadPolicyTests
{
    public static IEnumerable<object[]> Channels()
        => Enum.GetValues<BundleChannel>().Where(channel => channel != BundleChannel.Unknown).Select(channel => new object[] { channel });

    [Theory]
    [MemberData(nameof(Channels))]
    public void Select_ChannelAndPortabilityBeforeOwnership_OnlyEligibleOwnerRemains(BundleChannel channel)
    {
        WorkloadEntry expected = Content("custom.current", VersionFor(channel));
        WorkloadEntry[] otherChannels = [.. Enum.GetValues<BundleChannel>()
            .Where(other => other != BundleChannel.Unknown && other != channel)
            .Select(other => Content(TemplatesWorkloadConstants.GetPackageId("node"), VersionFor(other)))];
        WorkloadEntry invalid = Content("custom.invalid", VersionFor(channel), rid: "win-x64");

        IReadOnlyList<WorkloadEntry> selected = TemplatesWorkloadPolicy.Select([.. otherChannels, invalid, expected], "node", channel);

        selected.Should().ContainSingle().Which.Should().BeSameAs(expected);
    }

    [Theory]
    [MemberData(nameof(Channels))]
    public void Select_ConventionalIncumbent_WinsOverCompetingCustomOwners(BundleChannel channel)
    {
        WorkloadEntry expected = Content(TemplatesWorkloadConstants.GetPackageId("node"), VersionFor(channel), aliases: ["wrong"]);

        IReadOnlyList<WorkloadEntry> selected = TemplatesWorkloadPolicy.Select(
            [Content("custom.one", VersionFor(channel)), expected, Content("custom.two", VersionFor(channel))], "node", channel);

        selected.Should().ContainSingle().Which.Should().BeSameAs(expected);
    }

    [Theory]
    [InlineData("1.0.0", BundleChannel.Stable, true)]
    [InlineData("1.0.0+build.42", BundleChannel.Stable, true)]
    [InlineData("2.0.0-beta.1", BundleChannel.Stable, false)]
    [InlineData("2.0.0-rc.1", BundleChannel.Stable, false)]
    [InlineData("2.0.0-preview.1", BundleChannel.Stable, false)]
    [InlineData("2.0.0-preview.1", BundleChannel.Preview, true)]
    [InlineData("2.0.0-Preview.1", BundleChannel.Preview, true)]
    [InlineData("2.0.0-experimental.1", BundleChannel.Experimental, true)]
    [InlineData("2.0.0-beta.1", BundleChannel.Preview, false)]
    [InlineData("invalid-version", BundleChannel.Stable, false)]
    [InlineData("", BundleChannel.Preview, false)]
    [InlineData("invalid-version", null, false)]
    [InlineData("", null, false)]
    [InlineData("2.0.0-beta.1", null, true)]
    [InlineData("2.0.0-rc.1", null, true)]
    public void GetClaimants_OnlyValidVersionsMatchingRequestedChannel_AreEligible(
        string version, BundleChannel? channel, bool expected)
    {
        WorkloadEntry entry = Content("custom.templates", version);

        IReadOnlyList<WorkloadEntry> claims = TemplatesWorkloadPolicy.GetClaimants([entry], "node", channel);

        claims.Should().HaveCount(expected ? 1 : 0);
        TemplatesWorkloadPolicy.Select([entry], "node", channel).Should().BeEquivalentTo(claims);
    }

    [Theory]
    [InlineData("9.0.0-beta.1")]
    [InlineData("9.0.0-rc.1")]
    [InlineData("invalid-version")]
    public void Select_IneligibleConventionalOwner_DoesNotDisplaceStableCustomOwner(string version)
    {
        WorkloadEntry stable = Content("custom.stable", "1.0.0");
        WorkloadEntry conventional = Content(TemplatesWorkloadConstants.GetPackageId("node"), version);

        TemplatesWorkloadPolicy.Select([conventional, stable], "node", BundleChannel.Stable)
            .Should().ContainSingle().Which.Should().BeSameAs(stable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void PublicMethods_InvalidStack_Throw(string? stack)
    {
        Action select = () => TemplatesWorkloadPolicy.Select([], stack!);
        Action claims = () => TemplatesWorkloadPolicy.GetClaimants([], stack!, null);
        Action mismatch = () => TemplatesWorkloadPolicy.GetMismatch(Content("custom", "1.0.0"), stack!);

        select.Should().Throw<ArgumentException>();
        claims.Should().Throw<ArgumentException>();
        mismatch.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PublicMethods_NullDependencies_Throw()
    {
        Action select = () => TemplatesWorkloadPolicy.Select(null!, "node");
        Action claims = () => TemplatesWorkloadPolicy.GetClaimants(null!, "node", null);
        Action mismatch = () => TemplatesWorkloadPolicy.GetMismatch(null!, "node");

        select.Should().ThrowExactly<ArgumentNullException>();
        claims.Should().ThrowExactly<ArgumentNullException>();
        mismatch.Should().ThrowExactly<ArgumentNullException>();
    }

    private static string VersionFor(BundleChannel channel)
        => channel == BundleChannel.Stable ? "1.0.0" : $"1.0.0-{channel.ToPrereleaseLabel()}.1";

    private static WorkloadEntry Content(string id, string version, string? rid = null, string[]? aliases = null)
        => new()
        {
            PackageId = id, PackageVersion = version, Kind = WorkloadKind.Content,
            Aliases = aliases ?? ["node-templates"], RuntimeIdentifier = rid,
        };
}