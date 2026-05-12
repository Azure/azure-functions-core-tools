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
    private static readonly PackageSource _sourceA = new("https://a.test/v3/index.json", "a");
    private static readonly PackageSource _sourceB = new("https://b.test/v3/index.json", "b");

    [Fact]
    public async Task Search_AggregatesAcrossSources_KeepsHighestVersionPerId()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_sourceA, BuildClient(_sourceA, search: [Metadata("alpha", "1.0.0"), Metadata("beta", "1.0.0")])),
            (_sourceB, BuildClient(_sourceB, search: [Metadata("alpha", "2.0.0"), Metadata("gamma", "1.0.0")])));

        IReadOnlyList<CatalogSearchResult> results = await DrainAsync(catalog.Search(new CatalogSearchQuery()));

        Assert.Equal(3, results.Count);
        CatalogSearchResult alpha = Assert.Single(results, r => r.PackageId == "alpha");
        Assert.Equal(NuGetVersion.Parse("2.0.0"), alpha.LatestVersion);
        Assert.Equal(_sourceB.Name, alpha.Source.Name);
    }

    [Fact]
    public async Task Search_OrdersByVersionDescThenIdAsc()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_sourceA, BuildClient(_sourceA, search: [Metadata("zeta", "1.0.0"), Metadata("alpha", "2.0.0"), Metadata("beta", "1.0.0")])));

        IReadOnlyList<CatalogSearchResult> results = await DrainAsync(catalog.Search(new CatalogSearchQuery()));

        Assert.Equal(["alpha", "beta", "zeta"], results.Select(r => r.PackageId));
    }

    [Fact]
    public async Task Search_OverrideSource_ConsultsOnlyOverride()
    {
        var overrideSource = new PackageSource("https://override.test/v3/index.json", "override");
        NuGetProtocolSourceClient defaultClient = BuildClient(_sourceA, search: [Metadata("default-pkg", "1.0.0")]);
        NuGetProtocolSourceClient overrideClient = BuildClient(overrideSource, search: [Metadata("override-pkg", "1.0.0")]);

        var sourceProvider = Substitute.For<IPackageSourceProvider>();
        sourceProvider.GetSources(null).Returns([_sourceA]);
        sourceProvider.GetSources("https://override.test/v3/index.json").Returns([overrideSource]);

        var catalog = new WorkloadCatalog(sourceProvider, source =>
        {
            if (source.Name == _sourceA.Name)
            {
                return defaultClient;
            }

            if (source.Name == overrideSource.Name)
            {
                return overrideClient;
            }

            throw new InvalidOperationException($"Unexpected source {source.Name}");
        });

        IReadOnlyList<CatalogSearchResult> results = await DrainAsync(catalog.Search(
            new CatalogSearchQuery { OverrideSource = "https://override.test/v3/index.json" }));

        CatalogSearchResult only = Assert.Single(results);
        Assert.Equal("override-pkg", only.PackageId);
    }

    [Fact]
    public async Task ResolveLatestVersionAsync_PicksHighestStable_WhenPrereleaseDisabled()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_sourceA, BuildClient(_sourceA, versions: [V("1.0.0"), V("2.0.0-beta.1"), V("1.5.0")])));

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionAsync("alpha", includePrerelease: false);

        Assert.NotNull(resolved);
        Assert.Equal(V("1.5.0"), resolved!.Version);
        Assert.Equal(_sourceA.Name, resolved.Source.Name);
    }

    [Fact]
    public async Task ResolveLatestVersionAsync_IncludesPrerelease_WhenEnabled()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_sourceA, BuildClient(_sourceA, versions: [V("1.0.0"), V("2.0.0-beta.1"), V("1.5.0")])));

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionAsync("alpha", includePrerelease: true);

        Assert.Equal(V("2.0.0-beta.1"), resolved!.Version);
    }

    [Fact]
    public async Task ResolveLatestVersionAsync_ConstrainsToSameMajor_WhenAllowMajorFalse()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_sourceA, BuildClient(_sourceA, versions: [V("1.0.0"), V("1.5.0"), V("2.0.0"), V("2.1.0")])));

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionAsync(
            "alpha", includePrerelease: false, currentVersion: V("1.0.0"), allowMajor: false);

        Assert.Equal(V("1.5.0"), resolved!.Version);
    }

    [Fact]
    public async Task ResolveLatestVersionAsync_NoMatch_ReturnsNull()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_sourceA, BuildClient(_sourceA, versions: [V("1.0.0-beta")])));

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionAsync("alpha", includePrerelease: false);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveLatestVersionAsync_AggregatesAcrossSources()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_sourceA, BuildClient(_sourceA, versions: [V("1.0.0")])),
            (_sourceB, BuildClient(_sourceB, versions: [V("2.0.0")])));

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionAsync("alpha", includePrerelease: false);

        Assert.Equal(V("2.0.0"), resolved!.Version);
        Assert.Equal(_sourceB.Name, resolved.Source.Name);
    }

    [Fact]
    public async Task DownloadAsync_DelegatesToResolvedSource()
    {
        byte[] payload = [1, 2, 3];
        FindPackageByIdResource findA = Substitute.For<FindPackageByIdResource>();
        findA.CopyNupkgToStreamAsync(
                Arg.Any<string>(), Arg.Any<NuGetVersion>(), Arg.Any<Stream>(),
                Arg.Any<SourceCacheContext>(), Arg.Any<ILogger>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                Stream dest = ci.ArgAt<Stream>(2);
                await dest.WriteAsync(payload);
                return true;
            });
        FindPackageByIdResource findB = Substitute.For<FindPackageByIdResource>();

        WorkloadCatalog catalog = NewCatalog(
            (_sourceA, new NuGetProtocolSourceClient(TestRepository.Build(_sourceA, findA))),
            (_sourceB, new NuGetProtocolSourceClient(TestRepository.Build(_sourceB, findB))));

        await using Stream result = await catalog.DownloadAsync(new ResolvedPackage("alpha", V("1.0.0"), _sourceA));

        var copied = new MemoryStream();
        await result.CopyToAsync(copied);
        Assert.Equal(payload, copied.ToArray());
        await findB.DidNotReceive().CopyNupkgToStreamAsync(
            Arg.Any<string>(), Arg.Any<NuGetVersion>(), Arg.Any<Stream>(),
            Arg.Any<SourceCacheContext>(), Arg.Any<ILogger>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadAsync_NotFoundOnResolvedSource_Throws()
    {
        FindPackageByIdResource find = Substitute.For<FindPackageByIdResource>();
        find.CopyNupkgToStreamAsync(
                Arg.Any<string>(), Arg.Any<NuGetVersion>(), Arg.Any<Stream>(),
                Arg.Any<SourceCacheContext>(), Arg.Any<ILogger>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        WorkloadCatalog catalog = NewCatalog(
            (_sourceA, new NuGetProtocolSourceClient(TestRepository.Build(_sourceA, find))));

        await Assert.ThrowsAsync<WorkloadPackageNotFoundException>(
            () => catalog.DownloadAsync(new ResolvedPackage("alpha", V("1.0.0"), _sourceA)));
    }

    private static NuGetVersion V(string v) => NuGetVersion.Parse(v);

    private static async Task<IReadOnlyList<CatalogSearchResult>> DrainAsync(AsyncPageable<CatalogSearchResult> pageable)
    {
        List<CatalogSearchResult> results = [];
        await foreach (CatalogSearchResult item in pageable)
        {
            results.Add(item);
        }

        return results;
    }

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
        sourceProvider.GetSources(Arg.Any<string?>()).Returns([.. entries.Select(e => e.Source)]);

        var byName = entries.ToDictionary(e => e.Source.Name, e => e.Client, StringComparer.OrdinalIgnoreCase);
        return new WorkloadCatalog(sourceProvider, source => byName[source.Name]);
    }
}
