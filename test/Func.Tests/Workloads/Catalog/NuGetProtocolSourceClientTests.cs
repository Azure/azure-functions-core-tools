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

public sealed class NuGetProtocolSourceClientTests
{
    private static readonly PackageSource _source = new(
        "https://feed.test/v3/index.json",
        "test");

    [Fact]
    public async Task Search_PassesPackageTypeFilterAndPrereleaseFlag()
    {
        IPackageSearchMetadata[] hits = [Metadata("Workload.Python", "1.2.3", title: "Python", description: "py", tags: "alias:python language:python")];
        PackageSearchResource search = Substitute.For<PackageSearchResource>();
        SearchFilter? captured = null;
        search.SearchAsync(
                Arg.Any<string>(),
                Arg.Do<SearchFilter>(f => captured = f),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<ILogger>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<IPackageSearchMetadata>>(hits));

        NuGetProtocolSourceClient client = NewClient(searchResource: search);

        IReadOnlyList<CatalogSearchResult> results = await DrainAsync(client.Search(
            new CatalogSearchQuery
            {
                Filter = "python",
                IncludePrerelease = true,
                PageSize = 20,
                ContinuationToken = "10",
            }));

        Assert.NotNull(captured);
        Assert.True(captured!.IncludePrerelease);
        Assert.Equal(["FuncCliWorkload"], captured.PackageTypes);

        await search.Received(1).SearchAsync(
            "python",
            Arg.Any<SearchFilter>(),
            10,
            20,
            Arg.Any<ILogger>(),
            Arg.Any<CancellationToken>());

        CatalogSearchResult only = Assert.Single(results);
        Assert.Equal("workload.python", only.PackageId);
        Assert.Equal(NuGetVersion.Parse("1.2.3"), only.LatestVersion);
        Assert.Equal("Python", only.Title);
        Assert.Equal(["python"], only.Aliases);
        Assert.Same(_source, only.Source);
    }

    [Fact]
    public async Task Search_NullFilter_PassesEmptyString()
    {
        PackageSearchResource search = Substitute.For<PackageSearchResource>();
        search.SearchAsync(Arg.Any<string>(), Arg.Any<SearchFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<ILogger>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<IPackageSearchMetadata>>([]));

        NuGetProtocolSourceClient client = NewClient(searchResource: search);
        await DrainAsync(client.Search(new CatalogSearchQuery { PageSize = 10 }));

        await search.Received(1).SearchAsync(
            string.Empty,
            Arg.Any<SearchFilter>(),
            0,
            10,
            Arg.Any<ILogger>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Search_SkipsEntriesMissingIdentity()
    {
        IPackageSearchMetadata good = Metadata("Workload.A", "1.0.0");
        IPackageSearchMetadata bad = Substitute.For<IPackageSearchMetadata>();
        bad.Identity.Returns((PackageIdentity?)null);
        IPackageSearchMetadata[] hits = [good, bad];

        PackageSearchResource search = Substitute.For<PackageSearchResource>();
        search.SearchAsync(Arg.Any<string>(), Arg.Any<SearchFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<ILogger>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<IPackageSearchMetadata>>(hits));

        NuGetProtocolSourceClient client = NewClient(searchResource: search);
        IReadOnlyList<CatalogSearchResult> results = await DrainAsync(
            client.Search(new CatalogSearchQuery { PageSize = 10 }));

        Assert.Equal("workload.a", Assert.Single(results).PackageId);
    }

    [Fact]
    public async Task Search_ParsesAliasesFromTagsString()
    {
        IPackageSearchMetadata[] hits = [Metadata("Workload.Node", "1.0.0", tags: "alias:node language:javascript alias:nodejs")];
        PackageSearchResource search = Substitute.For<PackageSearchResource>();
        search.SearchAsync(Arg.Any<string>(), Arg.Any<SearchFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<ILogger>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<IPackageSearchMetadata>>(hits));

        NuGetProtocolSourceClient client = NewClient(searchResource: search);
        IReadOnlyList<CatalogSearchResult> results = await DrainAsync(
            client.Search(new CatalogSearchQuery { PageSize = 10 }));

        Assert.Equal(["node", "nodejs"], Assert.Single(results).Aliases);
    }

    [Fact]
    public async Task Search_YieldsMultiplePages_UntilShortPage()
    {
        // Two full pages of size 2 followed by a short page (1 item) -> 3 pages total.
        IPackageSearchMetadata[] page1 = [Metadata("Workload.A", "1.0.0"), Metadata("Workload.B", "1.0.0")];
        IPackageSearchMetadata[] page2 = [Metadata("Workload.C", "1.0.0"), Metadata("Workload.D", "1.0.0")];
        IPackageSearchMetadata[] page3 = [Metadata("Workload.E", "1.0.0")];

        PackageSearchResource search = Substitute.For<PackageSearchResource>();
        search.SearchAsync(Arg.Any<string>(), Arg.Any<SearchFilter>(), 0, 2, Arg.Any<ILogger>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<IPackageSearchMetadata>>(page1));
        search.SearchAsync(Arg.Any<string>(), Arg.Any<SearchFilter>(), 2, 2, Arg.Any<ILogger>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<IPackageSearchMetadata>>(page2));
        search.SearchAsync(Arg.Any<string>(), Arg.Any<SearchFilter>(), 4, 2, Arg.Any<ILogger>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<IPackageSearchMetadata>>(page3));

        NuGetProtocolSourceClient client = NewClient(searchResource: search);
        var pages = new List<Page<CatalogSearchResult>>();
        await foreach (Page<CatalogSearchResult> page in client.Search(new CatalogSearchQuery { PageSize = 2 }).AsPages())
        {
            pages.Add(page);
        }

        Assert.Equal(3, pages.Count);
        Assert.Equal("2", pages[0].ContinuationToken);
        Assert.Equal("4", pages[1].ContinuationToken);
        Assert.Null(pages[2].ContinuationToken);
        Assert.Equal(["workload.a", "workload.b", "workload.c", "workload.d", "workload.e"],
            pages.SelectMany(p => p.Values).Select(r => r.PackageId));
    }

    [Fact]
    public async Task ListVersionsAsync_ReturnsResolvedVersions()
    {
        FindPackageByIdResource find = Substitute.For<FindPackageByIdResource>();
        find.GetAllVersionsAsync(
                "Workload.Python",
                Arg.Any<SourceCacheContext>(),
                Arg.Any<ILogger>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<NuGetVersion>>(
            [
                NuGetVersion.Parse("1.0.0"),
                NuGetVersion.Parse("2.0.0"),
                NuGetVersion.Parse("2.0.0-beta.1"),
            ]));

        NuGetProtocolSourceClient client = NewClient(findResource: find);
        IReadOnlyList<NuGetVersion> versions = await client.ListVersionsAsync("Workload.Python", CancellationToken.None);

        Assert.Equal(
            [NuGetVersion.Parse("1.0.0"), NuGetVersion.Parse("2.0.0"), NuGetVersion.Parse("2.0.0-beta.1")],
            versions);
    }

    [Fact]
    public async Task ListVersionsAsync_NullEnumerable_ReturnsEmpty()
    {
        FindPackageByIdResource find = Substitute.For<FindPackageByIdResource>();
        find.GetAllVersionsAsync(Arg.Any<string>(), Arg.Any<SourceCacheContext>(), Arg.Any<ILogger>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<NuGetVersion>>(null!));

        NuGetProtocolSourceClient client = NewClient(findResource: find);
        Assert.Empty(await client.ListVersionsAsync("workload.absent", CancellationToken.None));
    }

    [Fact]
    public async Task OpenPackageAsync_ReturnsSeekableStreamWithBytes()
    {
        byte[] payload = [0x50, 0x4B, 0x03, 0x04]; // ZIP header bytes
        FindPackageByIdResource find = Substitute.For<FindPackageByIdResource>();
        find.CopyNupkgToStreamAsync(
                Arg.Any<string>(),
                Arg.Any<NuGetVersion>(),
                Arg.Any<Stream>(),
                Arg.Any<SourceCacheContext>(),
                Arg.Any<ILogger>(),
                Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                var dest = ci.ArgAt<Stream>(2);
                await dest.WriteAsync(payload);
                return true;
            });

        NuGetProtocolSourceClient client = NewClient(findResource: find);
        await using Stream stream = await client.OpenPackageAsync(
            "Workload.Python",
            NuGetVersion.Parse("1.2.3"),
            CancellationToken.None);

        Assert.True(stream.CanSeek);
        var copied = new MemoryStream();
        await stream.CopyToAsync(copied);
        Assert.Equal(payload, copied.ToArray());
    }

    [Fact]
    public async Task OpenPackageAsync_NotFound_ThrowsAndCleansUpTempFile()
    {
        FindPackageByIdResource find = Substitute.For<FindPackageByIdResource>();
        find.CopyNupkgToStreamAsync(
                Arg.Any<string>(),
                Arg.Any<NuGetVersion>(),
                Arg.Any<Stream>(),
                Arg.Any<SourceCacheContext>(),
                Arg.Any<ILogger>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        NuGetProtocolSourceClient client = NewClient(findResource: find);

        await Assert.ThrowsAsync<WorkloadPackageNotFoundException>(
            () => client.OpenPackageAsync("workload.absent", NuGetVersion.Parse("1.0.0"), CancellationToken.None));
    }

    private static NuGetProtocolSourceClient NewClient(
        PackageSearchResource? searchResource = null,
        FindPackageByIdResource? findResource = null)
    {
        SourceRepository repo = TestRepository.Build(_source, searchResource, findResource);
        return new NuGetProtocolSourceClient(repo);
    }

    private static async Task<IReadOnlyList<CatalogSearchResult>> DrainAsync(AsyncPageable<CatalogSearchResult> pageable)
    {
        List<CatalogSearchResult> results = [];
        await foreach (CatalogSearchResult item in pageable)
        {
            results.Add(item);
        }

        return results;
    }

    private static IPackageSearchMetadata Metadata(
        string id,
        string version,
        string? title = null,
        string? description = null,
        string? tags = null)
    {
        var meta = Substitute.For<IPackageSearchMetadata>();
        meta.Identity.Returns(new PackageIdentity(id, NuGetVersion.Parse(version)));
        meta.Title.Returns(title);
        meta.Description.Returns(description);
        meta.Tags.Returns(tags);
        return meta;
    }
}
