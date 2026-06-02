// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Update;
using Semver;
using Xunit;

namespace Azure.Functions.Cli.Tests.Update;

public sealed class RidResolverTests
{
    private static readonly string[] _allSupportedRids =
    [
        "win-x64",
        "win-arm64",
        "osx-x64",
        "osx-arm64",
        "linux-x64",
        "linux-arm64",
    ];

    [Fact]
    public void GetCurrentRid_ReturnsRidFromSupportedSet()
    {
        var resolver = new RidResolver();

        string rid = resolver.GetCurrentRid();

        Assert.Contains(rid, _allSupportedRids);
    }

    public static TheoryData<string, string> SupportedOsArchPairs() => new()
    {
        { nameof(OSPlatform.Windows), "win-x64" },
        { nameof(OSPlatform.Windows) + ":arm64", "win-arm64" },
        { nameof(OSPlatform.OSX), "osx-x64" },
        { nameof(OSPlatform.OSX) + ":arm64", "osx-arm64" },
        { nameof(OSPlatform.Linux), "linux-x64" },
        { nameof(OSPlatform.Linux) + ":arm64", "linux-arm64" },
    };

    [Theory]
    [MemberData(nameof(SupportedOsArchPairs))]
    public void Resolve_ReturnsExpectedRid(string osArchKey, string expectedRid)
    {
        (OSPlatform os, Architecture arch) = DecodeKey(osArchKey);

        string rid = RidResolver.Resolve(os, arch);

        Assert.Equal(expectedRid, rid);
    }

    [Fact]
    public void Resolve_UnsupportedOs_ThrowsGraceful()
    {
        GracefulException ex = Assert.Throws<GracefulException>(
            () => RidResolver.Resolve(OSPlatform.FreeBSD, Architecture.X64));

        Assert.True(ex.IsUserError);
        Assert.Contains("FREEBSD", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(Architecture.X86)]
    [InlineData(Architecture.Arm)]
    public void Resolve_UnsupportedArchitecture_ThrowsGraceful(Architecture arch)
    {
        GracefulException ex = Assert.Throws<GracefulException>(
            () => RidResolver.Resolve(OSPlatform.Linux, arch));

        Assert.True(ex.IsUserError);
        Assert.Contains(arch.ToString(), ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("win-x64", "func-win-x64.zip")]
    [InlineData("win-arm64", "func-win-arm64.zip")]
    [InlineData("osx-x64", "func-osx-x64.tar.gz")]
    [InlineData("osx-arm64", "func-osx-arm64.tar.gz")]
    [InlineData("linux-x64", "func-linux-x64.tar.gz")]
    [InlineData("linux-arm64", "func-linux-arm64.tar.gz")]
    public void SelectAsset_ReturnsMatchingAsset_ForEachSupportedRid(string rid, string expectedName)
    {
        var resolver = new RidResolver();
        Release release = CreateReleaseWithAllRids();

        ReleaseAsset? asset = resolver.SelectAsset(release, rid);

        Assert.NotNull(asset);
        Assert.Equal(expectedName, asset!.Name);
    }

    [Fact]
    public void SelectAsset_IsCaseInsensitive()
    {
        var resolver = new RidResolver();
        Release release = CreateReleaseWithAllRids();

        ReleaseAsset? asset = resolver.SelectAsset(release, "WIN-X64");

        Assert.NotNull(asset);
        Assert.Equal("func-win-x64.zip", asset!.Name);
    }

    [Fact]
    public void SelectAsset_ReturnsNull_WhenNoAssetMatches()
    {
        var resolver = new RidResolver();
        Release release = new(
            SemVersion.Parse("5.0.0", SemVersionStyles.Strict),
            IsPrerelease: false,
            TagName: "v5.0.0",
            Assets:
            [
                new ReleaseAsset("func-linux-x64.tar.gz", "https://example/linux", 1, Sha256: null),
            ]);

        ReleaseAsset? asset = resolver.SelectAsset(release, "win-x64");

        Assert.Null(asset);
    }

    [Fact]
    public void SelectAsset_PrefersZip_ForWindowsRid()
    {
        var resolver = new RidResolver();
        Release release = new(
            SemVersion.Parse("5.0.0", SemVersionStyles.Strict),
            IsPrerelease: false,
            TagName: "v5.0.0",
            Assets:
            [
                new ReleaseAsset("func-win-x64.tar.gz", "https://example/tar", 1, Sha256: null),
                new ReleaseAsset("func-win-x64.zip", "https://example/zip", 1, Sha256: null),
            ]);

        ReleaseAsset? asset = resolver.SelectAsset(release, "win-x64");

        Assert.NotNull(asset);
        Assert.Equal("func-win-x64.zip", asset!.Name);
    }

    [Fact]
    public void SelectAsset_PrefersTarGz_ForUnixRid()
    {
        var resolver = new RidResolver();
        Release release = new(
            SemVersion.Parse("5.0.0", SemVersionStyles.Strict),
            IsPrerelease: false,
            TagName: "v5.0.0",
            Assets:
            [
                new ReleaseAsset("func-linux-x64.zip", "https://example/zip", 1, Sha256: null),
                new ReleaseAsset("func-linux-x64.tar.gz", "https://example/tar", 1, Sha256: null),
            ]);

        ReleaseAsset? asset = resolver.SelectAsset(release, "linux-x64");

        Assert.NotNull(asset);
        Assert.Equal("func-linux-x64.tar.gz", asset!.Name);
    }

    [Fact]
    public void SelectAsset_NullRelease_Throws()
    {
        var resolver = new RidResolver();

        Assert.Throws<ArgumentNullException>(() => resolver.SelectAsset(null!, "win-x64"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void SelectAsset_EmptyOrNullRid_Throws(string? rid)
    {
        var resolver = new RidResolver();
        Release release = CreateReleaseWithAllRids();

        Assert.ThrowsAny<ArgumentException>(() => resolver.SelectAsset(release, rid!));
    }

    private static Release CreateReleaseWithAllRids() => new(
        SemVersion.Parse("5.0.0", SemVersionStyles.Strict),
        IsPrerelease: false,
        TagName: "v5.0.0",
        Assets:
        [
            new ReleaseAsset("func-win-x64.zip", "https://example/win-x64", 1, Sha256: null),
            new ReleaseAsset("func-win-arm64.zip", "https://example/win-arm64", 1, Sha256: null),
            new ReleaseAsset("func-osx-x64.tar.gz", "https://example/osx-x64", 1, Sha256: null),
            new ReleaseAsset("func-osx-arm64.tar.gz", "https://example/osx-arm64", 1, Sha256: null),
            new ReleaseAsset("func-linux-x64.tar.gz", "https://example/linux-x64", 1, Sha256: null),
            new ReleaseAsset("func-linux-arm64.tar.gz", "https://example/linux-arm64", 1, Sha256: null),
        ]);

    private static (OSPlatform Os, Architecture Arch) DecodeKey(string key)
    {
        string[] parts = key.Split(':');
        OSPlatform os = parts[0] switch
        {
            nameof(OSPlatform.Windows) => OSPlatform.Windows,
            nameof(OSPlatform.OSX) => OSPlatform.OSX,
            nameof(OSPlatform.Linux) => OSPlatform.Linux,
            _ => throw new InvalidOperationException($"Unhandled OS key '{parts[0]}'."),
        };
        Architecture arch = parts.Length > 1 && parts[1] == "arm64" ? Architecture.Arm64 : Architecture.X64;
        return (os, arch);
    }
}
