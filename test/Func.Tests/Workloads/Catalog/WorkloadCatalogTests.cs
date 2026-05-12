// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using NSubstitute;
using NuGet.Versioning;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Catalog;

public sealed class WorkloadCatalogTests
{
    private static readonly PackageSource _sourceA = new("a", new Uri("https://a.test/v3/index.json"), IsLocal: false);
    private static readonly PackageSource _sourceB = new("b", new Uri("https://b.test/v3/index.json"), IsLocal: false);

    [Fact]
    public async Task SearchAsync_AggregatesAcrossSources_KeepsHighestVersionPerId()
    {
        ISourceClient a = Substitute.For<ISourceClient>();
        a.Source.Returns(_sourceA);
        a.SearchAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                Result("alpha", "1.0.0", _sourceA),
                Result("beta", "1.0.0", _sourceA),
            ]);

        ISourceClient b = Substitute.For<ISourceClient>();
        b.Source.Returns(_sourceB);
        b.SearchAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                Result("alpha", "2.0.0", _sourceB),
                Result("gamma", "1.0.0", _sourceB),
            ]);

        WorkloadCatalog catalog = NewCatalog([a, b], _sourceA, _sourceB);

        IReadOnlyList<CatalogSearchResult> results = await catalog.SearchAsync(query: null, includePrerelease: false, skip: 0, take: 10);

        Assert.Equal(3, results.Count);
        CatalogSearchResult alpha = Assert.Single(results, r => r.PackageId == "alpha");
        Assert.Equal(NuGetVersion.Parse("2.0.0"), alpha.LatestVersion);
        Assert.Equal(_sourceB, alpha.Source);
    }

    [Fact]
    public async Task SearchAsync_OrdersByVersionDescThenIdAsc()
    {
        ISourceClient a = Substitute.For<ISourceClient>();
        a.Source.Returns(_sourceA);
        a.SearchAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                Result("zeta", "1.0.0", _sourceA),
                Result("alpha", "2.0.0", _sourceA),
                Result("beta", "1.0.0", _sourceA),
            ]);

        WorkloadCatalog catalog = NewCatalog([a], _sourceA);

        IReadOnlyList<CatalogSearchResult> results = await catalog.SearchAsync(null, false, 0, 10);

        Assert.Equal(["alpha", "beta", "zeta"], results.Select(r => r.PackageId));
    }

    [Fact]
    public async Task SearchAsync_OverrideSource_ConsultsOnlyOverride()
    {
        var overrideSource = new PackageSource("override", new Uri("https://override.test/v3/index.json"), IsLocal: false);
        ISourceClient overrideClient = Substitute.For<ISourceClient>();
        overrideClient.Source.Returns(overrideSource);
        overrideClient.SearchAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Result("override-pkg", "1.0.0", overrideSource)]);

        ISourceClient defaultClient = Substitute.For<ISourceClient>();
        defaultClient.Source.Returns(_sourceA);

        var sourceProvider = Substitute.For<IPackageSourceProvider>();
        sourceProvider.GetSources(null).Returns([_sourceA]);
        sourceProvider.GetSources("https://override.test/v3/index.json").Returns([overrideSource]);

        Func<PackageSource, ISourceClient> factory = source =>
            source == _sourceA ? defaultClient
            : source == overrideSource ? overrideClient
            : throw new InvalidOperationException($"Unexpected source {source.Name}");

        var catalog = new WorkloadCatalog(sourceProvider, factory);

        IReadOnlyList<CatalogSearchResult> results = await catalog.SearchAsync(null, false, 0, 10, overrideSource: "https://override.test/v3/index.json");

        CatalogSearchResult only = Assert.Single(results);
        Assert.Equal("override-pkg", only.PackageId);
        await defaultClient.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveLatestVersionAsync_PicksHighestStable_WhenPrereleaseDisabled()
    {
        ISourceClient a = SourceClientFor(_sourceA, [V("1.0.0"), V("2.0.0-beta.1"), V("1.5.0")]);
        WorkloadCatalog catalog = NewCatalog([a], _sourceA);

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionAsync("alpha", includePrerelease: false);

        Assert.NotNull(resolved);
        Assert.Equal(V("1.5.0"), resolved!.Version);
        Assert.Equal(_sourceA, resolved.Source);
    }

    [Fact]
    public async Task ResolveLatestVersionAsync_IncludesPrerelease_WhenEnabled()
    {
        ISourceClient a = SourceClientFor(_sourceA, [V("1.0.0"), V("2.0.0-beta.1"), V("1.5.0")]);
        WorkloadCatalog catalog = NewCatalog([a], _sourceA);

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionAsync("alpha", includePrerelease: true);

        Assert.Equal(V("2.0.0-beta.1"), resolved!.Version);
    }

    [Fact]
    public async Task ResolveLatestVersionAsync_ConstrainsToSameMajor_WhenAllowMajorFalse()
    {
        ISourceClient a = SourceClientFor(_sourceA, [V("1.0.0"), V("1.5.0"), V("2.0.0"), V("2.1.0")]);
        WorkloadCatalog catalog = NewCatalog([a], _sourceA);

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionAsync(
            "alpha", includePrerelease: false, currentVersion: V("1.0.0"), allowMajor: false);

        Assert.Equal(V("1.5.0"), resolved!.Version);
    }

    [Fact]
    public async Task ResolveLatestVersionAsync_NoMatch_ReturnsNull()
    {
        ISourceClient a = SourceClientFor(_sourceA, [V("1.0.0-beta")]);
        WorkloadCatalog catalog = NewCatalog([a], _sourceA);

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionAsync("alpha", includePrerelease: false);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveLatestVersionAsync_AggregatesAcrossSources()
    {
        ISourceClient a = SourceClientFor(_sourceA, [V("1.0.0")]);
        ISourceClient b = SourceClientFor(_sourceB, [V("2.0.0")]);
        WorkloadCatalog catalog = NewCatalog([a, b], _sourceA, _sourceB);

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionAsync("alpha", includePrerelease: false);

        Assert.Equal(V("2.0.0"), resolved!.Version);
        Assert.Equal(_sourceB, resolved.Source);
    }

    [Fact]
    public async Task DownloadAsync_DelegatesToResolvedSource()
    {
        var payload = new MemoryStream([1, 2, 3]);
        ISourceClient a = Substitute.For<ISourceClient>();
        a.Source.Returns(_sourceA);
        a.OpenPackageAsync("alpha", V("1.0.0"), Arg.Any<CancellationToken>()).Returns(payload);

        // Other source must not be touched even though it's also configured.
        ISourceClient b = Substitute.For<ISourceClient>();
        b.Source.Returns(_sourceB);

        WorkloadCatalog catalog = NewCatalog([a, b], _sourceA, _sourceB);

        Stream result = await catalog.DownloadAsync(new ResolvedPackage("alpha", V("1.0.0"), _sourceA));

        Assert.Same(payload, result);
        await b.DidNotReceive().OpenPackageAsync(Arg.Any<string>(), Arg.Any<NuGetVersion>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DownloadAsync_NotFoundOnResolvedSource_Throws()
    {
        ISourceClient a = Substitute.For<ISourceClient>();
        a.Source.Returns(_sourceA);
        a.OpenPackageAsync(Arg.Any<string>(), Arg.Any<NuGetVersion>(), Arg.Any<CancellationToken>())
            .Returns<Stream>(_ => throw new WorkloadPackageNotFoundException("nope"));

        WorkloadCatalog catalog = NewCatalog([a], _sourceA);

        await Assert.ThrowsAsync<WorkloadPackageNotFoundException>(
            () => catalog.DownloadAsync(new ResolvedPackage("alpha", V("1.0.0"), _sourceA)));
    }

    private static CatalogSearchResult Result(string id, string version, PackageSource source)
        => new(id, NuGetVersion.Parse(version), Title: id, Description: null, Aliases: [], Source: source);

    private static NuGetVersion V(string v) => NuGetVersion.Parse(v);

    private static ISourceClient SourceClientFor(PackageSource source, IReadOnlyList<NuGetVersion> versions)
    {
        ISourceClient client = Substitute.For<ISourceClient>();
        client.Source.Returns(source);
        client.ListVersionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(versions);
        return client;
    }

    private static WorkloadCatalog NewCatalog(IReadOnlyList<ISourceClient> clients, params PackageSource[] sources)
    {
        var sourceProvider = Substitute.For<IPackageSourceProvider>();
        sourceProvider.GetSources(Arg.Any<string?>()).Returns([.. sources]);

        Func<PackageSource, ISourceClient> factory = source =>
        {
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i] == source)
                {
                    return clients[i];
                }
            }

            throw new InvalidOperationException($"Unexpected source {source.Name}");
        };

        return new WorkloadCatalog(sourceProvider, factory);
    }
}
