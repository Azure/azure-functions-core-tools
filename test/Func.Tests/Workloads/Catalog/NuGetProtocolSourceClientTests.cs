// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NuGet.Common;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
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

        CatalogSearchResult only = NuGetProtocolSourceClient.ParseV3Hits(response, _source).Should().ContainSingle().Subject;
        only.PackageId.Should().Be("workloads.python");
        only.Aliases.Should().Equal(["python"]);
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

        NuGetProtocolSourceClient.ParseV3Hits(response, _source).Should().ContainSingle()
            .Which.Aliases.Should().Equal(["node", "nodejs"]);
    }

    [Fact]
    public void ParseV3Hits_ParsesKindTag()
    {
        var response = JObject.Parse("""
            {
              "data": [
                { "id": "Workloads.Python", "version": "1.0.0", "tags": "alias:python kind:workload" },
                { "id": "Workloads.Host",   "version": "1.0.0", "tags": [ "alias:host", "kind:content" ] },
                { "id": "Workloads.NoKind", "version": "1.0.0", "tags": "alias:misc" }
              ]
            }
            """);

        var results = NuGetProtocolSourceClient.ParseV3Hits(response, _source);

        results.Count.Should().Be(3);
        results.Single(r => r.PackageId == "workloads.python").Kind.Should().Be("workload");
        results.Single(r => r.PackageId == "workloads.host").Kind.Should().Be("content");
        results.Single(r => r.PackageId == "workloads.nokind").Kind.Should().BeNull();
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

        NuGetProtocolSourceClient.ParseV3Hits(response, _source).Should().BeEmpty();
    }

    [Fact]
    public void ParseV3Hits_ReturnsEmptyWhenDataMissing()
    {
        var response = JObject.Parse("""{ "totalHits": 0 }""");

        NuGetProtocolSourceClient.ParseV3Hits(response, _source).Should().BeEmpty();
    }

    [Fact]
    public void ParseV3Hits_FiltersHitsLackingFuncCliWorkloadPackageType()
    {
        // nuget.org ignores `packageType=` when q is empty, so an unfiltered
        // `func workload search` leaks arbitrary packages (issue #5198).
        // Defensive filter keeps hits that omit packageTypes (some feeds
        // don't include the field) but drops hits that declare other types.
        var response = JObject.Parse("""
            {
              "data": [
                {
                  "id": "Workloads.Python",
                  "version": "1.0.0",
                  "packageTypes": [ { "name": "FuncCliWorkload" } ]
                },
                {
                  "id": "Azure.Functions.Cli.Abstractions",
                  "version": "5.0.0",
                  "packageTypes": [ { "name": "Dependency" } ]
                },
                {
                  "id": "Workloads.NoPackageTypes",
                  "version": "1.0.0"
                }
              ]
            }
            """);

        var results = NuGetProtocolSourceClient.ParseV3Hits(response, _source);

        results.Count.Should().Be(2);
        results.Should().Contain(r => r.PackageId == "workloads.python");
        results.Should().Contain(r => r.PackageId == "workloads.nopackagetypes");
        results.Should().NotContain(r => r.PackageId == "azure.functions.cli.abstractions");
    }

    [Fact]
    public void ParseV3Hits_PackageTypeMatchIsCaseInsensitive()
    {
        var response = JObject.Parse("""
            {
              "data": [
                {
                  "id": "Workloads.Host",
                  "version": "1.0.0",
                  "packageTypes": [ { "name": "funccliworkload" } ]
                }
              ]
            }
            """);

        NuGetProtocolSourceClient.ParseV3Hits(response, _source).Should().ContainSingle();
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

        versions.Should().Equal([NuGetVersion.Parse("1.0.0"), NuGetVersion.Parse("2.0.0"), NuGetVersion.Parse("2.0.0-beta.1")]);
    }

    [Fact]
    public async Task ListVersionsAsync_NullEnumerable_ReturnsEmpty()
    {
        FindPackageByIdResource find = Substitute.For<FindPackageByIdResource>();
        find.GetAllVersionsAsync(Arg.Any<string>(), Arg.Any<SourceCacheContext>(), Arg.Any<ILogger>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<NuGetVersion>>(null!));

        NuGetProtocolSourceClient client = NewClient(findResource: find);
        (await client.ListVersionsAsync("workload.absent", CancellationToken.None)).Should().BeEmpty();
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

        stream.CanSeek.Should().BeTrue();
        var copied = new MemoryStream();
        await stream.CopyToAsync(copied);
        copied.ToArray().Should().Equal(payload);
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

        await FluentActions.Awaiting(() => client.OpenPackageAsync("workload.absent", NuGetVersion.Parse("1.0.0"), CancellationToken.None)).Should().ThrowAsync<WorkloadPackageNotFoundException>();
    }

    private static NuGetProtocolSourceClient NewClient(FindPackageByIdResource? findResource = null)
    {
        SourceRepository repo = TestRepository.Build(_source, findResource);
        return new NuGetProtocolSourceClient(repo);
    }
}
