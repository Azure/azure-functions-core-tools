// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NuGet.Common;
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
    public void ParseV3Hits_ParsesIdVersionAndAliasesFromTagsString()
    {
        var response = JObject.Parse("""
            {
              "data": [
                {
                  "id": "Workloads.Python",
                  "version": "1.2.3",
                  "tags": "alias:python language:python"
                }
              ]
            }
            """);

        CatalogSearchResult only = Assert.Single(NuGetProtocolSourceClient.ParseV3Hits(response, _source));
        Assert.Equal("workloads.python", only.PackageId);
        Assert.Equal(["python"], only.Aliases);
    }

    [Fact]
    public void ParseV3Hits_ParsesTagsArrayInAdditionToString()
    {
        var response = JObject.Parse("""
            {
              "data": [
                {
                  "id": "Workloads.Node",
                  "version": "1.0.0",
                  "tags": [ "alias:node", "language:javascript", "alias:nodejs" ]
                }
              ]
            }
            """);

        CatalogSearchResult only = Assert.Single(NuGetProtocolSourceClient.ParseV3Hits(response, _source));
        Assert.Equal(["node", "nodejs"], only.Aliases);
    }

    [Fact]
    public void ParseV3Hits_SkipsEntriesMissingIdOrVersion()
    {
        var response = JObject.Parse("""
            {
              "data": [
                { "version": "1.0.0" },
                { "id": "A" },
                { "id": "B", "version": "not-a-version" }
              ]
            }
            """);

        Assert.Empty(NuGetProtocolSourceClient.ParseV3Hits(response, _source));
    }

    [Fact]
    public void ParseV3Hits_ReturnsEmptyWhenDataMissing()
    {
        var response = JObject.Parse("""{ "totalHits": 0 }""");

        Assert.Empty(NuGetProtocolSourceClient.ParseV3Hits(response, _source));
    }


    [Fact]
    public async Task ListVersionsAsync_ReturnsResolvedVersions()
    {
        FindPackageByIdResource find = Substitute.For<FindPackageByIdResource>();
        find.GetAllVersionsAsync(
                "Workloads.Python",
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
        IReadOnlyList<NuGetVersion> versions = await client.ListVersionsAsync("Workloads.Python", CancellationToken.None);

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
        byte[] payload = [0x50, 0x4B, 0x03, 0x04];
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
            "Workloads.Python",
            NuGetVersion.Parse("1.2.3"),
            CancellationToken.None);

        Assert.True(stream.CanSeek);
        var copied = new MemoryStream();
        await stream.CopyToAsync(copied);
        Assert.Equal(payload, copied.ToArray());
    }

    [Fact]
    public async Task OpenPackageAsync_NotFound_Throws()
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

    private static NuGetProtocolSourceClient NewClient(FindPackageByIdResource? findResource = null)
    {
        SourceRepository repo = TestRepository.Build(_source, findResource);
        return new NuGetProtocolSourceClient(repo);
    }
}
