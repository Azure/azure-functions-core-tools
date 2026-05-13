// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using NSubstitute;
using NuGet.Common;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Xunit;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Tests.Workloads.Catalog;

public sealed class WorkloadCatalogTests
{
    private static readonly PackageSource _defaultSource = new("https://default.test/v3/index.json", "default");
    private static readonly PackageSource _altSource = new("https://override.test/v3/index.json", "override");

    [Fact]
    public async Task SearchAsync_DelegatesToConfiguredSource()
    {
        NuGetProtocolSourceClient client = BuildClient(_defaultSource, search: [Metadata("alpha", "1.0.0")]);
        WorkloadCatalog catalog = NewCatalog((_defaultSource, client));

        IReadOnlyList<CatalogSearchResult> results = await catalog.SearchAsync(new CatalogSearchQuery());

        CatalogSearchResult only = Assert.Single(results);
        Assert.Equal("alpha", only.PackageId);
        Assert.Equal(_defaultSource.Name, only.Source.Name);
    }

    [Fact]
    public async Task SearchAsync_Source_ConsultsOnlyOverride()
    {
        NuGetProtocolSourceClient defaultClient = BuildClient(_defaultSource, search: [Metadata("default-pkg", "1.0.0")]);
        NuGetProtocolSourceClient overrideClient = BuildClient(_altSource, search: [Metadata("override-pkg", "1.0.0")]);

        var sourceProvider = Substitute.For<IPackageSourceProvider>();
        sourceProvider.GetSource(null).Returns(_defaultSource);
        sourceProvider.GetSource(_altSource.Source).Returns(_altSource);

        var catalog = new WorkloadCatalog(sourceProvider, source => source.Name == _defaultSource.Name ? defaultClient : overrideClient);

        IReadOnlyList<CatalogSearchResult> results = await catalog.SearchAsync(
            new CatalogSearchQuery { Source = _altSource.Source });

        Assert.Equal("override-pkg", Assert.Single(results).PackageId);
    }

    [Fact]
    public async Task ResolveLatestVersionAsync_PicksHighestStable_WhenPrereleaseDisabled()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_defaultSource, BuildClient(_defaultSource, versions: [V("1.0.0"), V("2.0.0-beta.1"), V("1.5.0")])));

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionAsync("alpha", includePrerelease: false);

        Assert.NotNull(resolved);
        Assert.Equal(V("1.5.0"), resolved!.Version);
        Assert.Equal(_defaultSource.Name, resolved.Source.Name);
    }

    [Fact]
    public async Task ResolveLatestVersionAsync_IncludesPrerelease_WhenEnabled()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_defaultSource, BuildClient(_defaultSource, versions: [V("1.0.0"), V("2.0.0-beta.1"), V("1.5.0")])));

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionAsync("alpha", includePrerelease: true);

        Assert.Equal(V("2.0.0-beta.1"), resolved!.Version);
    }

    [Fact]
    public async Task ResolveLatestVersionAsync_ConstrainsToSameMajor_WhenAllowMajorFalse()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_defaultSource, BuildClient(_defaultSource, versions: [V("1.0.0"), V("1.5.0"), V("2.0.0"), V("2.1.0")])));

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionAsync(
            "alpha", includePrerelease: false, currentVersion: V("1.0.0"), allowMajor: false);

        Assert.Equal(V("1.5.0"), resolved!.Version);
    }

    [Fact]
    public async Task ResolveLatestVersionAsync_NoMatch_ReturnsNull()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_defaultSource, BuildClient(_defaultSource, versions: [V("1.0.0-beta")])));

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionAsync("alpha", includePrerelease: false);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveVersionAsync_ReturnsResolvedPackage_WhenSourceHasVersion()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_defaultSource, BuildClient(_defaultSource, versions: [V("1.0.0"), V("2.0.0")])));

        ResolvedPackage? resolved = await catalog.ResolveVersionAsync("alpha", V("2.0.0"));

        Assert.NotNull(resolved);
        Assert.Equal(V("2.0.0"), resolved!.Version);
        Assert.Equal(_defaultSource.Name, resolved.Source.Name);
    }

    [Fact]
    public async Task ResolveVersionAsync_VersionMissing_ReturnsNull()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_defaultSource, BuildClient(_defaultSource, versions: [V("1.0.0")])));

        ResolvedPackage? resolved = await catalog.ResolveVersionAsync("alpha", V("9.9.9"));

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveVersionAsync_LowercasesPackageId()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_defaultSource, BuildClient(_defaultSource, versions: [V("1.0.0")])));

        ResolvedPackage? resolved = await catalog.ResolveVersionAsync("Alpha", V("1.0.0"));

        Assert.Equal("alpha", resolved!.PackageId);
    }

    [Fact]
    public async Task DownloadAsync_DelegatesToResolvedSource()
    {
        byte[] payload = [1, 2, 3];
        FindPackageByIdResource find = Substitute.For<FindPackageByIdResource>();
        find.CopyNupkgToStreamAsync(
                Arg.Any<string>(), Arg.Any<NuGetVersion>(), Arg.Any<Stream>(),
                Arg.Any<SourceCacheContext>(), Arg.Any<ILogger>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                Stream dest = ci.ArgAt<Stream>(2);
                await dest.WriteAsync(payload);
                return true;
            });

        WorkloadCatalog catalog = NewCatalog(
            (_defaultSource, new NuGetProtocolSourceClient(TestRepository.Build(_defaultSource, find))));

        await using Stream result = await catalog.DownloadAsync(new ResolvedPackage("alpha", V("1.0.0"), _defaultSource));

        var copied = new MemoryStream();
        await result.CopyToAsync(copied);
        Assert.Equal(payload, copied.ToArray());
    }

    [Fact]
    public async Task DownloadAsync_NotFound_Throws()
    {
        FindPackageByIdResource find = Substitute.For<FindPackageByIdResource>();
        find.CopyNupkgToStreamAsync(
                Arg.Any<string>(), Arg.Any<NuGetVersion>(), Arg.Any<Stream>(),
                Arg.Any<SourceCacheContext>(), Arg.Any<ILogger>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        WorkloadCatalog catalog = NewCatalog(
            (_defaultSource, new NuGetProtocolSourceClient(TestRepository.Build(_defaultSource, find))));

        await Assert.ThrowsAsync<WorkloadPackageNotFoundException>(
            () => catalog.DownloadAsync(new ResolvedPackage("alpha", V("1.0.0"), _defaultSource)));
    }

    private static NuGetVersion V(string v) => NuGetVersion.Parse(v);

    private static IPackageSearchMetadata Metadata(string id, string version, string? tags = null)
    {
        IPackageSearchMetadata m = Substitute.For<IPackageSearchMetadata>();
        m.Identity.Returns(new PackageIdentity(id, NuGetVersion.Parse(version)));
        m.Tags.Returns(tags);
        return m;
    }

    private static NuGetProtocolSourceClient BuildClient(
        PackageSource source,
        IPackageSearchMetadata[]? search = null,
        IEnumerable<NuGetVersion>? versions = null)
    {
        PackageSearchResource? searchResource = null;
        if (search is not null)
        {
            searchResource = Substitute.For<PackageSearchResource>();
            searchResource.SearchAsync(Arg.Any<string>(), Arg.Any<SearchFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<ILogger>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IEnumerable<IPackageSearchMetadata>>(search));
        }

        FindPackageByIdResource? findResource = null;
        if (versions is not null)
        {
            findResource = Substitute.For<FindPackageByIdResource>();
            findResource.GetAllVersionsAsync(Arg.Any<string>(), Arg.Any<SourceCacheContext>(), Arg.Any<ILogger>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(versions));
        }

        return new NuGetProtocolSourceClient(TestRepository.Build(source, searchResource, findResource));
    }

    private static WorkloadCatalog NewCatalog(params (PackageSource Source, NuGetProtocolSourceClient Client)[] entries)
    {
        var sourceProvider = Substitute.For<IPackageSourceProvider>();
        sourceProvider.GetSource(Arg.Any<string?>()).Returns(entries[0].Source);

        var byName = entries.ToDictionary(e => e.Source.Name, e => e.Client, StringComparer.OrdinalIgnoreCase);
        return new WorkloadCatalog(sourceProvider, source => byName[source.Name]);
    }
}
