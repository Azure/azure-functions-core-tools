// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Catalog;

public sealed class PackageSourceProviderTests : IDisposable
{
    private readonly string _tempRoot = Directory.CreateTempSubdirectory("workload-source-provider-").FullName;

    public void Dispose() => Directory.Delete(_tempRoot, recursive: true);

    [Fact]
    public void GetSources_OverrideUrl_ReturnsSingleRemoteSource()
    {
        PackageSourceProvider provider = NewProvider();

        IReadOnlyList<PackageSource> sources = provider.GetSources("https://example.test/v3/index.json");

        PackageSource only = Assert.Single(sources);
        Assert.False(only.IsLocal);
        Assert.Equal("https://example.test/v3/index.json", only.Location.AbsoluteUri);
    }

    [Fact]
    public void GetSources_OverrideLocalDir_ReturnsLocalSource()
    {
        string folder = Path.Combine(_tempRoot, "feed");
        Directory.CreateDirectory(folder);
        PackageSourceProvider provider = NewProvider();

        IReadOnlyList<PackageSource> sources = provider.GetSources(folder);

        PackageSource only = Assert.Single(sources);
        Assert.True(only.IsLocal);
        Assert.Equal(Path.GetFullPath(folder), only.Location.LocalPath.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void GetSources_OverrideShortCircuitsConfiguredSources()
    {
        PackageSourceProvider provider = NewProvider("https://other.test/v3/index.json");

        IReadOnlyList<PackageSource> sources = provider.GetSources("https://override.test/v3/index.json");

        PackageSource only = Assert.Single(sources);
        Assert.Equal("https://override.test/v3/index.json", only.Location.AbsoluteUri);
    }

    [Fact]
    public void GetSources_NoOverride_ReturnsConfiguredSourcesInOrder()
    {
        string folder = Path.Combine(_tempRoot, "feed");
        Directory.CreateDirectory(folder);
        PackageSourceProvider provider = NewProvider(
            "https://first.test/v3/index.json",
            folder);

        IReadOnlyList<PackageSource> sources = provider.GetSources();

        Assert.Equal(2, sources.Count);
        Assert.False(sources[0].IsLocal);
        Assert.Equal("https://first.test/v3/index.json", sources[0].Location.AbsoluteUri);
        Assert.True(sources[1].IsLocal);
    }

    [Fact]
    public void GetSources_EmptyAndWhitespaceEntries_AreSkipped()
    {
        PackageSourceProvider provider = NewProvider(string.Empty, "   ", "https://kept.test/v3/index.json");

        IReadOnlyList<PackageSource> sources = provider.GetSources();

        PackageSource only = Assert.Single(sources);
        Assert.Equal("https://kept.test/v3/index.json", only.Location.AbsoluteUri);
    }

    [Fact]
    public void GetSources_NoOverride_NoConfigured_ReturnsNuGetOrgDefault()
    {
        PackageSourceProvider provider = NewProvider();

        IReadOnlyList<PackageSource> sources = provider.GetSources();

        PackageSource only = Assert.Single(sources);
        Assert.False(only.IsLocal);
        Assert.Equal(PackageSourceProvider.DefaultSourceUrl, only.Location.AbsoluteUri);
    }

    [Fact]
    public void GetSources_OverrideMissingDirectory_Throws()
    {
        PackageSourceProvider provider = NewProvider();

        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => provider.GetSources(Path.Combine(_tempRoot, "does-not-exist")));
        Assert.Contains("does not exist", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetSources_ConfiguredInvalidEntry_Throws()
    {
        PackageSourceProvider provider = NewProvider("ftp://unsupported.example/feed");

        Assert.Throws<ArgumentException>(() => provider.GetSources());
    }

    private static PackageSourceProvider NewProvider(params string[] sources)
    {
        var options = Options.Create(new WorkloadCatalogOptions { Sources = [.. sources] });
        return new PackageSourceProvider(options);
    }
}
