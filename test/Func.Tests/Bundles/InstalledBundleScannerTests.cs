// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Versioning;
using Xunit;

namespace Azure.Functions.Cli.Bundles.Tests;

public class InstalledBundleScannerTests
{
    private const string StableId = InstalledBundleScanner.StableBundleId;
    private const string PreviewId = InstalledBundleScanner.PreviewBundleId;
    private const string ExperimentalId = InstalledBundleScanner.ExperimentalBundleId;

    [Fact]
    public async Task NoRows_ReportsZeroTotal()
    {
        ScanResult result = await Build([]).ScanAsync(StableId);
        Assert.Equal(0, result.TotalInstalledRows);
        Assert.Empty(result.FilteredVersions);
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

        Assert.Equal(4, result.TotalInstalledRows);
        Assert.Equal(2, result.FilteredVersions.Count);
        Assert.Equal(NuGetVersion.Parse("4.24.0"), result.FilteredVersions[0].Version);
        Assert.Equal(NuGetVersion.Parse("4.22.0"), result.FilteredVersions[1].Version);
    }

    [Fact]
    public async Task StableId_AcceptsFourPartWorkloadVersion()
    {
        ScanResult result = await Build(
        [
            Row("4.35.0.1", "/a"),
            Row("4.35.0.2", "/b"),
        ]).ScanAsync(StableId);

        Assert.Equal(2, result.FilteredVersions.Count);
        Assert.Equal(NuGetVersion.Parse("4.35.0.2"), result.FilteredVersions[0].Version);
    }

    [Fact]
    public async Task PreviewId_FiltersToPreviewLabelOnly()
    {
        ScanResult result = await Build(
        [
            Row("4.22.0", "/a"),
            Row("4.23.0-preview.1", "/b"),
            Row("4.24.0-experimental.1", "/c"),
            Row("4.25.0-beta.1", "/d"),
        ]).ScanAsync(PreviewId);

        Assert.Single(result.FilteredVersions);
        Assert.Equal(NuGetVersion.Parse("4.23.0-preview.1"), result.FilteredVersions[0].Version);
    }

    [Fact]
    public async Task PreviewId_MatchesFourPartWithBareLabel()
    {
        ScanResult result = await Build(
        [
            Row("4.35.0.1-preview", "/a"),
            Row("4.35.0.2-preview", "/b"),
            Row("4.35.0.1", "/c"),
        ]).ScanAsync(PreviewId);

        Assert.Equal(2, result.FilteredVersions.Count);
        Assert.Equal(NuGetVersion.Parse("4.35.0.2-preview"), result.FilteredVersions[0].Version);
        Assert.Equal(NuGetVersion.Parse("4.35.0.1-preview"), result.FilteredVersions[1].Version);
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

        Assert.Equal(2, result.FilteredVersions.Count);
        Assert.Equal(NuGetVersion.Parse("4.25.0-experimental.2"), result.FilteredVersions[0].Version);
        Assert.Equal(NuGetVersion.Parse("4.24.0-experimental.1"), result.FilteredVersions[1].Version);
    }

    [Fact]
    public async Task PayloadPath_IsInstallDirSlashToolsSlashAny()
    {
        ScanResult result = await Build([Row("4.35.0", "/install/dir")]).ScanAsync(StableId);

        Assert.Equal(Path.Combine("/install/dir", "tools", "any"), result.FilteredVersions[0].Path);
    }

    [Fact]
    public async Task NonSemverVersionsAreSkipped()
    {
        ScanResult result = await Build(
        [
            new InstalledBundleWorkload("garbage", "/x"),
            Row("4.22.0", "/y"),
        ]).ScanAsync(StableId);

        Assert.Single(result.FilteredVersions);
        Assert.Equal(NuGetVersion.Parse("4.22.0"), result.FilteredVersions[0].Version);
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

        Assert.Equal(NuGetVersion.Parse("4.30.0"), result.FilteredVersions[0].Version);
        Assert.Equal(NuGetVersion.Parse("4.10.0"), result.FilteredVersions[2].Version);
    }

    private static InstalledBundleScanner Build(IReadOnlyList<InstalledBundleWorkload> rows)
        => new(new FakeInstalledBundleWorkloads(rows));

    private static InstalledBundleWorkload Row(string version, string installDir)
        => new(version, installDir);
}
