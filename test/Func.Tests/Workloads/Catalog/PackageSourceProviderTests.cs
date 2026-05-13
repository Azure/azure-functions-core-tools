// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.Extensions.Options;
using Xunit;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Tests.Workloads.Catalog;

public sealed class PackageSourceProviderTests : IDisposable
{
    private readonly string _tempRoot = Directory.CreateTempSubdirectory("workload-source-provider-").FullName;

    public void Dispose() => Directory.Delete(_tempRoot, recursive: true);

    [Fact]
    public void GetSource_OverrideUrl_ReturnsRemoteSource()
    {
        PackageSourceProvider provider = NewProvider();

        PackageSource source = provider.GetSource("https://example.test/v3/index.json");

        Assert.False(source.IsLocal);
        Assert.Equal("https://example.test/v3/index.json", source.Source);
    }

    [Fact]
    public void GetSource_OverrideLocalDir_ReturnsLocalSource()
    {
        string folder = Path.Combine(_tempRoot, "feed");
        Directory.CreateDirectory(folder);
        PackageSourceProvider provider = NewProvider();

        PackageSource source = provider.GetSource(folder);

        Assert.True(source.IsLocal);
        Assert.Equal(Path.GetFullPath(folder), source.Source);
    }

    [Fact]
    public void GetSource_OverrideShortCircuitsConfiguredSource()
    {
        PackageSourceProvider provider = NewProvider("https://other.test/v3/index.json");

        PackageSource source = provider.GetSource("https://override.test/v3/index.json");

        Assert.Equal("https://override.test/v3/index.json", source.Source);
    }

    [Fact]
    public void GetSource_NoOverride_ReturnsConfiguredSource()
    {
        PackageSourceProvider provider = NewProvider("https://configured.test/v3/index.json");

        PackageSource source = provider.GetSource();

        Assert.Equal("https://configured.test/v3/index.json", source.Source);
    }

    [Fact]
    public void GetSource_NoOverride_NoConfigured_ReturnsNuGetOrgDefault()
    {
        PackageSourceProvider provider = NewProvider();

        PackageSource source = provider.GetSource();

        Assert.False(source.IsLocal);
        Assert.Equal(PackageSourceProvider.DefaultSourceUrl, source.Source);
    }

    [Fact]
    public void GetSource_OverrideMissingDirectory_Throws()
    {
        PackageSourceProvider provider = NewProvider();

        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => provider.GetSource(Path.Combine(_tempRoot, "does-not-exist")));
        Assert.Contains("does not exist", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetSource_ConfiguredInvalidEntry_Throws()
    {
        PackageSourceProvider provider = NewProvider("ftp://unsupported.example/feed");

        Assert.Throws<ArgumentException>(() => provider.GetSource());
    }

    private static PackageSourceProvider NewProvider(string? source = null)
    {
        var options = Options.Create(new WorkloadCatalogOptions { Source = source });
        return new PackageSourceProvider(options);
    }
}
