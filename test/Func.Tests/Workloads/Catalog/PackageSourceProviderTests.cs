// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.Extensions.Options;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Tests.Workloads.Catalog;

public sealed class PackageSourceProviderTests
{
    [Fact]
    public void GetSource_OverrideUrl_ReturnsRemoteSource()
    {
        PackageSourceProvider provider = NewProvider();

        PackageSource source = provider.GetSource("https://example.test/v3/index.json");

        source.IsLocal.Should().BeFalse();
        source.Source.Should().Be("https://example.test/v3/index.json");
    }

    [Fact]
    public void GetSource_OverrideShortCircuitsConfiguredSource()
    {
        PackageSourceProvider provider = NewProvider("https://other.test/v3/index.json");

        PackageSource source = provider.GetSource("https://override.test/v3/index.json");

        source.Source.Should().Be("https://override.test/v3/index.json");
    }

    [Fact]
    public void GetSource_NoOverride_ReturnsConfiguredSource()
    {
        PackageSourceProvider provider = NewProvider("https://configured.test/v3/index.json");

        PackageSource source = provider.GetSource();

        source.Source.Should().Be("https://configured.test/v3/index.json");
    }

    [Fact]
    public void GetSource_NoOverride_NoConfigured_ReturnsNuGetOrgDefault()
    {
        PackageSourceProvider provider = NewProvider();

        PackageSource source = provider.GetSource();

        source.IsLocal.Should().BeFalse();
        source.Source.Should().Be(PackageSourceProvider.DefaultSourceUrl);
    }

    [Theory]
    [InlineData("/some/local/folder")]
    [InlineData("./relative")]
    [InlineData("file:///tmp/feed")]
    [InlineData("ftp://unsupported.example/feed")]
    public void GetSource_NonHttpSource_Throws(string value)
    {
        PackageSourceProvider provider = NewProvider();

        ArgumentException ex = FluentActions.Invoking(() => provider.GetSource(value)).Should().ThrowExactly<ArgumentException>().Which;
        ex.Message.Should().ContainEquivalentOf("not a supported NuGet feed");
    }

    [Fact]
    public void GetSource_ConfiguredNonHttp_Throws()
    {
        PackageSourceProvider provider = NewProvider("ftp://unsupported.example/feed");

        FluentActions.Invoking(() => provider.GetSource()).Should().ThrowExactly<ArgumentException>();
    }

    private static PackageSourceProvider NewProvider(string? source = null)
    {
        return new PackageSourceProvider(Options.Create(new WorkloadCatalogOptions { Source = source }));
    }
}
