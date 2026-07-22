// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Versioning;

namespace Azure.Functions.Cli.Bundles.Tests;

public class InstalledBundleScannerTests
{
    private const string StableId = BundleHelpers.StableBundleId;
    private const string PreviewId = BundleHelpers.PreviewBundleId;
    private const string ExperimentalId = BundleHelpers.ExperimentalBundleId;

    [Fact]
    public async Task NoRows_ReportsZeroTotal()
    {
        ScanResult result = await Build([]).ScanAsync(StableId);
        result.TotalInstalledRows.Should().Be(0);
        result.FilteredVersions.Should().BeEmpty();
    }

    [Fact]
    public async Task StableId_FiltersOutPrereleaseVersions()
    {
        ScanResult result = await Build(
        [
            Row("4.22.0", "/a"),
            Row("4.23.0-preview", "/b"),
            Row("4.23.0-experimental", "/c"),
            Row("4.24.0", "/d"),
        ]).ScanAsync(StableId);

        result.TotalInstalledRows.Should().Be(4);
        result.FilteredVersions.Count.Should().Be(2);
        result.FilteredVersions[0].Version.Should().Be(NuGetVersion.Parse("4.24.0"));
        result.FilteredVersions[1].Version.Should().Be(NuGetVersion.Parse("4.22.0"));
    }

    [Fact]
    public async Task PreviewId_FiltersToPreviewLabelOnly()
    {
        ScanResult result = await Build(
        [
            Row("4.22.0", "/a"),
            Row("4.23.0-preview", "/b"),
            Row("4.24.0-experimental", "/c"),
            Row("4.25.0-beta", "/d"),
        ]).ScanAsync(PreviewId);

        result.FilteredVersions.Should().ContainSingle();
        result.FilteredVersions[0].Version.Should().Be(NuGetVersion.Parse("4.23.0-preview"));
    }

    [Fact]
    public async Task ExperimentalId_FiltersToExperimentalLabelOnly()
    {
        ScanResult result = await Build(
        [
            Row("4.22.0", "/a"),
            Row("4.23.0-preview.1", "/b"),
            Row("4.24.0-experimental.1", "/c"),
            Row("4.25.0-experimental.2", "/d"),
        ]).ScanAsync(ExperimentalId);

        result.FilteredVersions.Count.Should().Be(2);
        result.FilteredVersions[0].Version.Should().Be(NuGetVersion.Parse("4.25.0-experimental.2"));
        result.FilteredVersions[1].Version.Should().Be(NuGetVersion.Parse("4.24.0-experimental.1"));
    }

    [Fact]
    public async Task PayloadPath_IsInstallDirSlashToolsSlashAny()
    {
        ScanResult result = await Build([Row("4.35.0", "/install/dir")]).ScanAsync(StableId);

        result.FilteredVersions[0].Path.Should().Be(Path.Combine("/install/dir", "tools", "any"));
    }

    [Fact]
    public async Task NonSemverVersionsAreSkipped()
    {
        ScanResult result = await Build(
        [
            new InstalledBundleWorkload("garbage", "/x"),
            Row("4.22.0", "/y"),
        ]).ScanAsync(StableId);

        result.FilteredVersions.Should().ContainSingle();
        result.FilteredVersions[0].Version.Should().Be(NuGetVersion.Parse("4.22.0"));
    }

    [Fact]
    public async Task SortedHighestFirst()
    {
        ScanResult result = await Build(
        [
            Row("4.20.0", "/a"),
            Row("4.30.0", "/b"),
            Row("4.10.0", "/c"),
        ]).ScanAsync(StableId);

        result.FilteredVersions[0].Version.Should().Be(NuGetVersion.Parse("4.30.0"));
        result.FilteredVersions[2].Version.Should().Be(NuGetVersion.Parse("4.10.0"));
    }

    private static InstalledBundleScanner Build(IReadOnlyList<InstalledBundleWorkload> rows)
        => new(new FakeInstalledBundleWorkloads(rows));

    private static InstalledBundleWorkload Row(string version, string installDir)
        => new(version, installDir);
}
