// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Packages;
using Azure.Functions.Cli.Tests.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Tests.Templates.Packages;

public sealed class TemplateCatalogTests
{
    [Fact]
    public void Options_UseOnlyTemplateSourceAndCliReleaseStatus()
    {
        var environment = Substitute.For<IProcessEnvironment>();
        environment.Get("FUNC_CLI_TEMPLATES_SOURCE").Returns("https://templates.test/v3/index.json");
        environment.Get("FUNC_CLI_WORKLOADS_SOURCE").Returns("https://workloads.test/v3/index.json");
        environment.Get("FUNC_CLI_WORKLOADS_PRERELEASE").Returns("true");
        var version = Substitute.For<ICliVersionProvider>();
        version.IsPrerelease.Returns(false);
        var options = new TemplateCatalogOptions();

        new TemplateCatalogOptionsSetup(environment, version).Configure(options);

        options.Source.Should().Be("https://templates.test/v3/index.json");
        options.IncludePrerelease.Should().BeFalse();
        environment.Received(1).Get("FUNC_CLI_TEMPLATES_SOURCE");
        environment.DidNotReceive().Get("FUNC_CLI_WORKLOADS_SOURCE");
        environment.DidNotReceive().Get("FUNC_CLI_WORKLOADS_PRERELEASE");
    }

    [Theory]
    [InlineData(null, null, "https://api.nuget.org/v3/index.json")]
    [InlineData(null, "  ", "https://api.nuget.org/v3/index.json")]
    [InlineData(null, "https://env.test/v3/index.json", "https://env.test/v3/index.json")]
    [InlineData("https://cli.test/v3/index.json", "https://env.test/v3/index.json", "https://cli.test/v3/index.json")]
    public void Source_UsesIndependentPrecedence(string? explicitSource, string? environmentSource, string expected)
    {
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions { Source = environmentSource }),
            source => new NuGetProtocolSourceClient(TestRepository.Build(source)));

        catalog.GetSource(explicitSource).Source.Should().Be(expected);
    }

    [Theory]
    [InlineData("file:///packages")]
    [InlineData("C:\\packages")]
    [InlineData("ftp://feed.test/index.json")]
    [InlineData("")]
    [InlineData("  ")]
    public void Source_RejectsNonHttpFeeds(string source)
    {
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()), _ => throw new InvalidOperationException());

        Action act = () => catalog.GetSource(source);

        act.Should().Throw<ArgumentException>().WithMessage("*absolute http(s) URL*V3 service index*");
    }

    [Theory]
    [InlineData(false, "1.0.0")]
    [InlineData(true, "2.0.0-preview.1")]
    public async Task ResolveVersion_PrefersStableUnlessRequested(bool prerelease, string expected)
    {
        var versions = Substitute.For<FindPackageByIdResource>();
        versions.GetAllVersionsAsync(Arg.Any<string>(), Arg.Any<SourceCacheContext>(), Arg.Any<NuGet.Common.ILogger>(),
            Arg.Any<CancellationToken>()).Returns([NuGetVersion.Parse("1.0.0"), NuGetVersion.Parse("2.0.0-preview.1")]);
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions { IncludePrerelease = true }),
            source => new NuGetProtocolSourceClient(TestRepository.Build(source, versions)));

        var result = await catalog.ResolveVersionAsync("test.templates", null, "https://feed.test/index.json", prerelease, CancellationToken.None);

        result.ToNormalizedString().Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null, "https://api.nuget.org/v3/index.json")]
    [InlineData(null, "https://env.test/index.json", "https://env.test/index.json")]
    [InlineData("http://explicit.test/index.json", "https://env.test/index.json", "http://explicit.test/index.json")]
    public async Task CatalogOperations_UseOnlyTheResolvedFeed(string? explicitSource, string? environmentSource, string expected)
    {
        byte[] payload = [1, 2, 3];
        var version = NuGetVersion.Parse("2.0.0");
        List<string> requestedSources = [];
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions { Source = environmentSource }), source =>
        {
            requestedSources.Add(source.Source);
            var resource = Substitute.For<FindPackageByIdResource>();
            resource.GetAllVersionsAsync("test.templates", Arg.Any<SourceCacheContext>(), Arg.Any<NuGet.Common.ILogger>(),
                Arg.Any<CancellationToken>()).Returns([version]);
            resource.CopyNupkgToStreamAsync(
                "test.templates", version, Arg.Any<Stream>(), Arg.Any<SourceCacheContext>(),
                Arg.Any<NuGet.Common.ILogger>(), Arg.Any<CancellationToken>()).Returns(async call =>
                {
                    await call.ArgAt<Stream>(2).WriteAsync(payload, call.ArgAt<CancellationToken>(5));
                    return true;
                });
            var index = new ServiceIndexResourceV3(JObject.Parse("""
                {"version":"3.0.0","resources":[{"@id":"https://search.test/query","@type":"SearchQueryService"}]}
                """), DateTime.UtcNow);
            return new SearchClient(TestRepository.Build(source, resource, index), JObject.Parse("""{"data":[]}"""));
        });

        var results = await catalog.SearchAsync(null, explicitSource, false, TestContext.Current.CancellationToken);
        var resolved = await catalog.ResolveVersionAsync("test.templates", null, explicitSource, false, TestContext.Current.CancellationToken);
        await using Stream archive = await catalog.OpenPackageAsync("test.templates", resolved, explicitSource, TestContext.Current.CancellationToken);

        using var bytes = new MemoryStream();
        await archive.CopyToAsync(bytes, TestContext.Current.CancellationToken);
        bytes.ToArray().Should().Equal(payload);
        archive.CanSeek.Should().BeTrue();
        resolved.Should().Be(version);
        results.Should().BeEmpty();
        requestedSources.Should().Equal(expected, expected, expected);
    }

    [Fact]
    public async Task OpenPackage_MissingVersionReportsTemplateFailureRatherThanWorkloadFailure()
    {
        var resource = Substitute.For<FindPackageByIdResource>();
        resource.CopyNupkgToStreamAsync(Arg.Any<string>(), Arg.Any<NuGetVersion>(), Arg.Any<Stream>(), Arg.Any<SourceCacheContext>(),
            Arg.Any<NuGet.Common.ILogger>(), Arg.Any<CancellationToken>()).Returns(false);
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()),
            source => new NuGetProtocolSourceClient(TestRepository.Build(source, resource)));

        Func<Task> act = () => catalog.OpenPackageAsync("test.templates", NuGetVersion.Parse("1.0.0"), null, CancellationToken.None);

        await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*Template package*not found*");
    }

    [Theory]
    [InlineData("1.*", false)]
    [InlineData("[1.0,2.0)", false)]
    [InlineData("1.0.0", true)]
    public async Task ResolveVersion_RejectsRangesAndConflictingOptions(string version, bool prerelease)
    {
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()), _ => throw new InvalidOperationException());

        Func<Task> act = () => catalog.ResolveVersionAsync("test.templates", version, "https://feed.test/index.json", prerelease, CancellationToken.None);

        await act.Should().ThrowAsync<TemplatePackageException>();
    }

    [Fact]
    public async Task ResolveVersion_AcceptsFourPartNuGetVersion()
    {
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()),
            _ => throw new InvalidOperationException());

        NuGetVersion version = await catalog.ResolveVersionAsync(
            "test.templates", "1.2.3.4", "https://feed.test/index.json", false, TestContext.Current.CancellationToken);

        version.ToNormalizedString().Should().Be("1.2.3.4");
    }

    [Fact]
    public async Task Search_FiltersServerAndDeclaredTypesWithoutExpandingAndCapsResults()
    {
        var source = new PackageSource("https://feed.test/index.json");
        var index = new ServiceIndexResourceV3(JObject.Parse("""
            {"version":"3.0.0","resources":[{"@id":"https://search.test/query","@type":"SearchQueryService"}]}
            """), DateTime.UtcNow);
        var response = new JObject
        {
            ["data"] = new JArray(Enumerable.Range(0, 25).Select(i => new JObject
            {
                ["id"] = $"test.templates{i}",
                ["version"] = "1.0.0",
                ["packageTypes"] = new JArray(new JObject { ["name"] = "FuncTemplate" }),
            })),
        };
        response["data"]![0]!["packageTypes"] = new JArray("FuncCliWorkload");
        var client = new SearchClient(TestRepository.Build(source, index), response);
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()), _ => client);

        var results = await catalog.SearchAsync("hello world", null, false, CancellationToken.None);

        client.SearchUri!.Query.Should().Contain("packageType=FuncTemplate").And.Contain("take=20").And.Contain("q=hello%20world");
        results.Should().HaveCount(20).And.NotContain(r => r.PackageId == "test.templates0");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Search_MalformedResponseFailsWhileWorkloadCompatibilityIsPreserved(bool nullResponse)
    {
        var source = new PackageSource("https://feed.test/index.json");
        var index = new ServiceIndexResourceV3(JObject.Parse("""
            {"version":"3.0.0","resources":[{"@id":"https://search.test/query","@type":"SearchQueryService"}]}
            """), DateTime.UtcNow);
        var client = new SearchClient(TestRepository.Build(source, index), nullResponse ? null : JObject.Parse("{}"));
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()), _ => client);

        Func<Task> act = () => catalog.SearchAsync(null, null, false, CancellationToken.None);

        await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*invalid response*");
        (await client.SearchAsync(new CatalogSearchQuery(), CancellationToken.None)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("17")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"version":"1.0.0"}""")]
    [InlineData("""{"id":"test.templates"}""")]
    [InlineData("""{"id":"invalid/id","version":"1.0.0"}""")]
    [InlineData("""{"id":"   ","version":"1.0.0"}""")]
    [InlineData("""{"id":"test.templates","version":"invalid"}""")]
    [InlineData("""{"id":17,"version":"1.0.0"}""")]
    [InlineData("""{"id":"test.templates","version":17}""")]
    [InlineData("""{"id":"test.templates","version":"1.0.0","title":17}""")]
    [InlineData("""{"id":"test.templates","version":"1.0.0","description":[]}""")]
    [InlineData("""{"id":"test.templates","version":"1.0.0","packageTypes":{}}""")]
    [InlineData("""{"id":"test.templates","version":"1.0.0","packageTypes":[null]}""")]
    public async Task Search_MalformedRowsThrowDocumentedDomainFailure(string row)
    {
        var client = new TextSearchClient(SearchRepository(), $$"""{"data":[{{row}}]}""");
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()), _ => client);

        Func<Task> act = () => catalog.SearchAsync(null, null, false, CancellationToken.None);

        var failure = (await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*invalid response*")).Which;
        failure.InnerException.Should().BeOfType<InvalidDataException>();
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"data":[""")]
    [InlineData("[]")]
    public async Task Search_MalformedJsonPreservesParsingExceptionInDomainFailure(string json)
    {
        var client = new TextSearchClient(SearchRepository(), json);
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()), _ => client);

        Func<Task> act = () => catalog.SearchAsync(null, null, false, CancellationToken.None);

        var failure = (await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*malformed JSON*")).Which;
        failure.InnerException.Should().BeAssignableTo<Newtonsoft.Json.JsonException>();
    }

    [Fact]
    public async Task Search_FiltersIncompatibleDeclaredTypesBeforeValidatingExcludedMetadata()
    {
        var client = new TextSearchClient(SearchRepository(), """
            {"data":[
              {"packageTypes":[{"name":"Dependency"}]},
              {"id":17,"version":null,"packageTypes":["FuncCliWorkload"]},
              {"id":"test.templates","version":"1.0.0"}
            ]}
            """);
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()), _ => client);

        var results = await catalog.SearchAsync(null, null, false, CancellationToken.None);

        results.Should().ContainSingle().Which.PackageId.Should().Be("test.templates");
    }

    [Fact]
    public async Task ResolveVersion_DefaultRejectsAPrereleaseOnlyFeed()
    {
        var resource = Substitute.For<FindPackageByIdResource>();
        resource.GetAllVersionsAsync(Arg.Any<string>(), Arg.Any<SourceCacheContext>(), Arg.Any<NuGet.Common.ILogger>(),
            Arg.Any<CancellationToken>()).Returns([NuGetVersion.Parse("1.0.0-preview.1")]);
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()),
            source => new NuGetProtocolSourceClient(TestRepository.Build(source, resource)));

        Func<Task> act = () => catalog.ResolveVersionAsync("test.templates", null, null, false, CancellationToken.None);

        await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*No matching version*--prerelease*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Install_FeedFailureIsReportedWithoutFallback(bool download)
    {
        int requests = 0;
        var resource = Substitute.For<FindPackageByIdResource>();
        resource.GetAllVersionsAsync(Arg.Any<string>(), Arg.Any<SourceCacheContext>(), Arg.Any<NuGet.Common.ILogger>(),
            Arg.Any<CancellationToken>()).Returns(Task.FromException<IEnumerable<NuGetVersion>>(new FatalProtocolException("feed unavailable")));
        resource.CopyNupkgToStreamAsync(Arg.Any<string>(), Arg.Any<NuGetVersion>(), Arg.Any<Stream>(), Arg.Any<SourceCacheContext>(),
            Arg.Any<NuGet.Common.ILogger>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<bool>(new FatalProtocolException("feed unavailable")));
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions { Source = "https://env.test/index.json" }), source =>
        {
            requests++;
            source.Source.Should().Be("https://explicit.test/index.json");
            return new NuGetProtocolSourceClient(TestRepository.Build(source, resource));
        });

        Func<Task> act = () => download
            ? catalog.OpenPackageAsync("test.templates", NuGetVersion.Parse("1.0.0"), "https://explicit.test/index.json", TestContext.Current.CancellationToken)
            : catalog.ResolveVersionAsync("test.templates", null, "https://explicit.test/index.json", false, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*Cannot read template feed*explicit.test*");
        requests.Should().Be(1);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("2.0.0-preview.1")]
    public async Task ResolveVersion_ExactVersionDoesNotQueryTheFeed(string version)
    {
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()), _ => throw new InvalidOperationException());

        var result = await catalog.ResolveVersionAsync("test.templates", version, null, false, TestContext.Current.CancellationToken);

        result.Should().Be(NuGetVersion.Parse(version));
    }

    [Theory]
    [InlineData(null, "file:///packages")]
    [InlineData("file:///packages", "https://env.test/index.json")]
    [InlineData("", "https://env.test/index.json")]
    public async Task CatalogOperations_InvalidSourceFailsBeforeAcquisition(string? explicitSource, string environmentSource)
    {
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions { Source = environmentSource }),
            _ => throw new InvalidOperationException());

        Func<Task> search = () => catalog.SearchAsync(null, explicitSource, false, TestContext.Current.CancellationToken);
        Func<Task> resolve = () => catalog.ResolveVersionAsync("test.templates", null, explicitSource, false, TestContext.Current.CancellationToken);
        Func<Task> exact = () => catalog.ResolveVersionAsync("test.templates", "1.0.0", explicitSource, false, TestContext.Current.CancellationToken);
        Func<Task> download = () => catalog.OpenPackageAsync("test.templates", NuGetVersion.Parse("1.0.0"), explicitSource,
            TestContext.Current.CancellationToken);

        await search.Should().ThrowAsync<ArgumentException>();
        await resolve.Should().ThrowAsync<ArgumentException>();
        await exact.Should().ThrowAsync<ArgumentException>();
        await download.Should().ThrowAsync<ArgumentException>();
    }

    private static SourceRepository SearchRepository()
    {
        var index = new ServiceIndexResourceV3(JObject.Parse("""
            {"version":"3.0.0","resources":[{"@id":"https://search.test/query","@type":"SearchQueryService"}]}
            """), DateTime.UtcNow);
        return TestRepository.Build(new PackageSource("https://feed.test/index.json"), index);
    }

    private sealed class TextSearchClient(SourceRepository repository, string json) : NuGetProtocolSourceClient(repository)
    {
        internal override Task<JObject?> FetchSearchResponseAsync(Uri searchUri, CancellationToken cancellationToken)
            => Task.FromResult<JObject?>(JObject.Parse(json));
    }

    private sealed class SearchClient(SourceRepository repository, JObject? response) : NuGetProtocolSourceClient(repository)
    {
        internal Uri? SearchUri { get; private set; }

        internal override Task<JObject?> FetchSearchResponseAsync(Uri searchUri, CancellationToken cancellationToken)
        {
            SearchUri = searchUri;
            return Task.FromResult<JObject?>(response);
        }
    }
}
