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
            source => new NuGetProtocolSourceClient(TestRepository.Build(source)), Sources());

        catalog.GetSource(explicitSource).Source.Should().Be(expected);
    }

    [Theory]
    [InlineData("file:///packages")]
    [InlineData("C:\\packages")]
    [InlineData("ftp://feed.test/index.json")]
    public void Source_RejectsNonHttpFeeds(string source)
    {
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()), _ => throw new InvalidOperationException(), Sources());

        Action act = () => catalog.GetSource(source);

        act.Should().Throw<ArgumentException>();
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
            source => new NuGetProtocolSourceClient(TestRepository.Build(source, versions)), Sources());

        var result = await catalog.ResolveVersionAsync("test.templates", null, "https://feed.test/index.json", prerelease, CancellationToken.None);

        result.ToNormalizedString().Should().Be(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenPackage_UsesNextEligibleFeedWhenTheFirstCannotSupplyTheExactVersion(bool unavailable)
    {
        var sources = Sources();
        sources.GetSources(Arg.Any<string>(), Arg.Any<PackageSource?>()).Returns(
            [new PackageSource("https://first.test/index.json"), new PackageSource("https://second.test/index.json")]);
        byte[] payload = [1, 2, 3];
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()), source =>
        {
            var resource = Substitute.For<FindPackageByIdResource>();
            resource.CopyNupkgToStreamAsync(
                "test.templates", NuGetVersion.Parse("2.0.0"), Arg.Any<Stream>(), Arg.Any<SourceCacheContext>(),
                Arg.Any<NuGet.Common.ILogger>(), Arg.Any<CancellationToken>()).Returns(async call =>
                {
                    if (source.Source.Contains("first", StringComparison.Ordinal))
                    {
                        if (unavailable)
                        {
                            throw new FatalProtocolException("feed unavailable");
                        }

                        return false;
                    }

                    await call.ArgAt<Stream>(2).WriteAsync(payload, call.ArgAt<CancellationToken>(5));
                    return true;
                });
            return new NuGetProtocolSourceClient(TestRepository.Build(source, resource));
        }, sources);

        await using Stream archive = await catalog.OpenPackageAsync("test.templates", NuGetVersion.Parse("2.0.0"), null, CancellationToken.None);

        using var bytes = new MemoryStream();
        await archive.CopyToAsync(bytes, TestContext.Current.CancellationToken);
        bytes.ToArray().Should().Equal(payload);
        archive.CanSeek.Should().BeTrue();
    }

    [Fact]
    public async Task OpenPackage_MissingVersionReportsTemplateFailureRatherThanWorkloadFailure()
    {
        var resource = Substitute.For<FindPackageByIdResource>();
        resource.CopyNupkgToStreamAsync(Arg.Any<string>(), Arg.Any<NuGetVersion>(), Arg.Any<Stream>(), Arg.Any<SourceCacheContext>(),
            Arg.Any<NuGet.Common.ILogger>(), Arg.Any<CancellationToken>()).Returns(false);
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()),
            source => new NuGetProtocolSourceClient(TestRepository.Build(source, resource)), Sources());

        Func<Task> act = () => catalog.OpenPackageAsync("test.templates", NuGetVersion.Parse("1.0.0"), null, CancellationToken.None);

        await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*Template package*not found*");
    }

    [Theory]
    [InlineData("1.*", false)]
    [InlineData("1.0", false)]
    [InlineData("1.0.0.0", false)]
    [InlineData("[1.0,2.0)", false)]
    [InlineData("1.0.0", true)]
    public async Task ResolveVersion_RejectsRangesAndConflictingOptions(string version, bool prerelease)
    {
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()), _ => throw new InvalidOperationException(), Sources());

        Func<Task> act = () => catalog.ResolveVersionAsync("test.templates", version, "https://feed.test/index.json", prerelease, CancellationToken.None);

        await act.Should().ThrowAsync<TemplatePackageException>();
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
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()), _ => client, Sources());

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
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()), _ => client, Sources());

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
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()), _ => client, Sources());

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
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()), _ => client, Sources());

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
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()), _ => client, Sources());

        var results = await catalog.SearchAsync(null, null, false, CancellationToken.None);

        results.Should().ContainSingle().Which.PackageId.Should().Be("test.templates");
    }

    private static TemplateNuGetSourceProvider Sources()
    {
        var sources = Substitute.For<TemplateNuGetSourceProvider>(new DirectoryInfo(Directory.GetCurrentDirectory()));
        sources.GetSources(Arg.Any<string>(), Arg.Any<PackageSource?>()).Returns(c =>
            [c.ArgAt<PackageSource?>(1) ?? new PackageSource("https://feed.test/index.json")]);
        return sources;
    }

    [Fact]
    public void AdditionalSource_IsAbsentWithoutAnExplicitOrEnvironmentFeed()
    {
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()), _ => throw new InvalidOperationException(), Sources());

        catalog.GetAdditionalSource(null).Should().BeNull();
    }

    [Fact]
    public async Task ResolveVersion_DefaultRejectsAPrereleaseOnlyFeed()
    {
        var resource = Substitute.For<FindPackageByIdResource>();
        resource.GetAllVersionsAsync(Arg.Any<string>(), Arg.Any<SourceCacheContext>(), Arg.Any<NuGet.Common.ILogger>(),
            Arg.Any<CancellationToken>()).Returns([NuGetVersion.Parse("1.0.0-preview.1")]);
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()),
            source => new NuGetProtocolSourceClient(TestRepository.Build(source, resource)), Sources());

        Func<Task> act = () => catalog.ResolveVersionAsync("test.templates", null, null, false, CancellationToken.None);

        await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*No matching version*");
    }

    [Fact]
    public void SourceProvider_NullWorkingDirectoryIsRejected()
    {
        Action act = () => _ = new TemplateNuGetSourceProvider(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("workingDirectory");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResolveVersion_ToleratesUnavailableFeedsOnlyWhenAnotherFeedCanBeRead(bool readableFeed)
    {
        var sources = Sources();
        sources.GetSources(Arg.Any<string>(), Arg.Any<PackageSource?>()).Returns(
            [new PackageSource("https://unavailable.test/index.json"), new PackageSource("https://available.test/index.json")]);
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()), source =>
        {
            var resource = Substitute.For<FindPackageByIdResource>();
            resource.GetAllVersionsAsync(Arg.Any<string>(), Arg.Any<SourceCacheContext>(), Arg.Any<NuGet.Common.ILogger>(),
                Arg.Any<CancellationToken>()).Returns(readableFeed && source.Source.Contains("//available.", StringComparison.Ordinal)
                    ? Task.FromResult<IEnumerable<NuGetVersion>>([NuGetVersion.Parse("1.0.0")])
                    : Task.FromException<IEnumerable<NuGetVersion>>(new FatalProtocolException("feed unavailable")));
            return new NuGetProtocolSourceClient(TestRepository.Build(source, resource));
        }, sources);

        if (readableFeed)
        {
            (await catalog.ResolveVersionAsync("test.templates", null, null, false, CancellationToken.None))
                .ToNormalizedString().Should().Be("1.0.0");
        }
        else
        {
            Func<Task> act = () => catalog.ResolveVersionAsync("test.templates", null, null, false, CancellationToken.None);
            await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*Cannot read template feed*");
        }
    }

    [Theory]
    [InlineData(false, "2.0.0")]
    [InlineData(true, "3.0.0-preview.1")]
    public async Task ResolveVersion_UsesAmbientAndAdditionalFeedsWithoutPreviewFallback(bool prerelease, string expected)
    {
        var sources = Sources();
        sources.GetSources(Arg.Any<string>(), Arg.Any<PackageSource?>()).Returns(
            [new PackageSource("https://ambient.test/index.json"), new PackageSource("https://extra.test/index.json")]);
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()), source =>
        {
            var resource = Substitute.For<FindPackageByIdResource>();
            resource.GetAllVersionsAsync(Arg.Any<string>(), Arg.Any<SourceCacheContext>(), Arg.Any<NuGet.Common.ILogger>(),
                Arg.Any<CancellationToken>()).Returns(source.Source.Contains("ambient", StringComparison.Ordinal)
                    ? [NuGetVersion.Parse("1.0.0")] : [NuGetVersion.Parse("2.0.0"), NuGetVersion.Parse("3.0.0-preview.1")]);
            return new NuGetProtocolSourceClient(TestRepository.Build(source, resource));
        }, sources);

        NuGetVersion version = await catalog.ResolveVersionAsync("test.templates", null, "https://extra.test/index.json", prerelease, CancellationToken.None);

        version.ToNormalizedString().Should().Be(expected);
    }

    [Fact]
    public void InstallSources_RespectMappingsDisabledSourcesAndHttpFilteringButAppendAdditionalFeed()
    {
        string root = Path.Combine(Directory.GetCurrentDirectory(), "template-sources-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "NuGet.Config"), """
                <configuration>
                  <packageSources><clear />
                    <add key="mapped" value="https://mapped.test/index.json" />
                    <add key="unmapped" value="https://unmapped.test/index.json" />
                    <add key="http" value="http://insecure.test/index.json" />
                    <add key="disabled" value="https://disabled.test/index.json" />
                  </packageSources>
                  <disabledPackageSources><clear /><add key="disabled" value="true" /></disabledPackageSources>
                  <packageSourceMapping><clear />
                    <packageSource key="mapped"><package pattern="test.*" /></packageSource>
                    <packageSource key="http"><package pattern="test.*" /></packageSource>
                    <packageSource key="disabled"><package pattern="test.*" /></packageSource>
                  </packageSourceMapping>
                </configuration>
                """);
            var provider = new TemplateNuGetSourceProvider(new DirectoryInfo(root));

            var result = provider.GetSources("test.templates", new PackageSource("https://extra.test/index.json"));

            result.Select(s => s.Source).Should().Equal("https://extra.test/index.json", "https://mapped.test/index.json");
            provider.GetSources("other.templates", new PackageSource("https://extra.test/index.json"))
                .Select(s => s.Source).Should().Equal("https://extra.test/index.json");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
