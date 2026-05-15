// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.Extensions.Options;
using Xunit;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Tests.Workloads.Catalog;

public sealed class PackageSourceProviderTests
{
    [Fact]
    public void GetSource_OverrideUrl_ReturnsRemoteSource()
    {
        PackageSourceProvider provider = NewProvider();

        PackageSource source = provider.GetSource("https://example.test/v3/index.json");

        Assert.False(source.IsLocal);
        Assert.Equal("https://example.test/v3/index.json", source.Source);
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

    [Theory]
    [InlineData("/some/local/folder")]
    [InlineData("./relative")]
    [InlineData("file:///tmp/feed")]
    [InlineData("ftp://unsupported.example/feed")]
    public void GetSource_NonHttpSource_Throws(string value)
    {
        PackageSourceProvider provider = NewProvider();

        ArgumentException ex = Assert.Throws<ArgumentException>(() => provider.GetSource(value));
        Assert.Contains("not a supported NuGet feed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetSource_ConfiguredNonHttp_Throws()
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
