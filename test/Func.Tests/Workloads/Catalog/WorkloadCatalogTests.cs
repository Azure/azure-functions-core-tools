// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Install;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NuGet.Common;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Tests.Workloads.Catalog;

public sealed class WorkloadCatalogTests
{
    private static readonly PackageSource _defaultSource = new("https://default.test/v3/index.json", "default");
    private static readonly PackageSource _altSource = new("https://override.test/v3/index.json", "override");

    [Fact]
    public async Task SearchAsync_DelegatesToConfiguredSource()
    {
        NuGetProtocolSourceClient client = BuildClient(_defaultSource, search: [("alpha", "1.0.0")]);
        WorkloadCatalog catalog = NewCatalog((_defaultSource, client));

        IReadOnlyList<CatalogSearchResult> results = await catalog.SearchAsync(
            new CatalogSearchQuery(), TestContext.Current.CancellationToken);

        CatalogSearchResult only = results.Should().ContainSingle().Subject;
        only.PackageId.Should().Be("alpha");
        only.Source.Name.Should().Be(_defaultSource.Name);
    }

    [Fact]
    public async Task SearchAsync_Source_ConsultsOnlyOverride()
    {
        NuGetProtocolSourceClient defaultClient = BuildClient(_defaultSource, search: [("default-pkg", "1.0.0")]);
        NuGetProtocolSourceClient overrideClient = BuildClient(_altSource, search: [("override-pkg", "1.0.0")]);

        var sourceProvider = Substitute.For<IPackageSourceProvider>();
        sourceProvider.GetSource(null).Returns(_defaultSource);
        sourceProvider.GetSource(_altSource.Source).Returns(_altSource);

        var catalog = new WorkloadCatalog(
            Options.Create(new WorkloadCatalogOptions()),
            sourceProvider,
            source => source.Name == _defaultSource.Name ? defaultClient : overrideClient);

        IReadOnlyList<CatalogSearchResult> results = await catalog.SearchAsync(
            new CatalogSearchQuery { Source = _altSource.Source }, TestContext.Current.CancellationToken);

        results.Should().ContainSingle().Subject.PackageId.Should().Be("override-pkg");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SearchVersionsAsync_UsesTheSelectedSourcesListedVersionMetadataAcrossPages(bool includePrerelease)
    {
        const string id = "Contoso.DevKit";
        var metadata = Substitute.For<PackageMetadataResource>();
        IPackageSearchMetadata[] versions =
        [
            VersionMetadata("0.9.0", "Before stacks", "kind:workload alias:comet"),
            VersionMetadata("1.0.0", "Comet", "kind:workload alias:legacy alias:comet stack:comet worker-runtime:comet"),
            VersionMetadata("2.0.0-preview.1", "Comet Next", "kind:workload alias:comet stack:comet worker-runtime:native"),
        ];
        metadata.GetMetadataAsync(id, includePrerelease, false, Arg.Any<SourceCacheContext>(), Arg.Any<ILogger>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<IPackageSearchMetadata>>(versions));

        var repo = TestRepository.Build(_altSource, NewServiceIndex(), metadata);
        var offsets = new List<int>();
        var client = new FakeClient(repo, response: null, uri =>
        {
            var offset = int.Parse(System.Web.HttpUtility.ParseQueryString(uri.Query)["skip"]!);
            offsets.Add(offset);
            var hit = offset == 0
                ? JObject.Parse("""{"id":"Unrelated","version":"4.0.0","packageTypes":[{"name":"Dependency"}]}""")
                : JObject.Parse("""{"id":"Contoso.DevKit","version":"2.0.0-preview.1","title":"Latest hit, not the stable title"}""");
            return new JObject { ["data"] = new JArray(hit), ["totalHits"] = 2 };
        });
        var provider = Substitute.For<IPackageSourceProvider>();
        provider.GetSource(_altSource.Source).Returns(_altSource);
        var catalog = new WorkloadCatalog(Options.Create(new WorkloadCatalogOptions { IncludePrerelease = true }), provider, source =>
        {
            source.Should().BeSameAs(_altSource);
            return client;
        });

        var rows = await catalog.SearchVersionsAsync(new CatalogSearchQuery { Source = _altSource.Source, Take = 1, IncludePrerelease = includePrerelease }, TestContext.Current.CancellationToken);

        offsets.Should().Equal(0, 1);
        rows.Should().OnlyContain(row => row.PackageId == id && row.Source == _altSource);
        rows.Single(row => row.LatestVersion == V("0.9.0")).Stack.Should().BeNull();
        var stable = rows.Single(row => row.LatestVersion == V("1.0.0"));
        stable.Stack!.StackIdentifier.Should().Be("comet");
        stable.Stack.DisplayName.Should().Be("Comet");
        stable.Stack.WorkerRuntime.Should().Be("comet");
        stable.Stack.Description.Should().Be("Comet tooling");
        stable.Aliases.Should().Equal("legacy", "comet");
        if (includePrerelease)
        {
            var preview = rows.Single(row => row.LatestVersion.IsPrerelease);
            preview.Stack!.DisplayName.Should().Be("Comet Next");
            preview.Stack.WorkerRuntime.Should().Be("native");
            preview.Stack.Description.Should().Be("Comet Next tooling");
        }
        else
        {
            rows.Should().NotContain(row => row.LatestVersion.IsPrerelease);
        }

        static IPackageSearchMetadata VersionMetadata(string version, string title, string tags)
        {
            var result = Substitute.For<IPackageSearchMetadata>();
            result.Identity.Returns(new PackageIdentity(id, V(version)));
            result.IsListed.Returns(true);
            result.Tags.Returns(tags);
            result.Title.Returns(title);
            result.Description.Returns($"{title} tooling");
            return result;
        }
    }

    [Fact]
    public async Task SearchVersionsAsync_UnavailableMetadataSource_IsNotAnEmptyInventory()
    {
        var catalog = NewCatalog((_defaultSource, BuildClient(_defaultSource, search: [("Contoso.DevKit", "1.0.0")])));

        var act = () => catalog.SearchVersionsAsync(new(), TestContext.Current.CancellationToken);

        await act.Should().ThrowExactlyAsync<WorkloadCatalogException>().WithMessage($"*{_defaultSource.Source}*");
    }

    [Fact]
    public async Task ResolveAlias_ConflictingOwnersOnDifferentFeedPages_RequiresExactPackageId()
    {
        var client = new FakeClient(TestRepository.Build(_defaultSource, NewServiceIndex()), null, uri =>
        {
            string id = uri.Query.Contains("skip=0&", StringComparison.Ordinal) ? "Contoso.DevKit" : "Another.Owner";
            JObject page = SearchResponse((id, "1.0.0"));
            page["totalHits"] = 2;
            page["data"]![0]!["tags"] = "kind:workload stack:comet worker-runtime:comet alias:comet";
            page["data"]![0]!["title"] = "Comet";
            return page;
        });
        var source = new WorkloadPackageSource(NewCatalog((_defaultSource, client)), Substitute.For<IWorkloadPackageInspector>(), Options.Create(new WorkloadCatalogOptions()));

        var act = () => source.ResolveAsync("comet", null, _defaultSource.Source, false, false, TestContext.Current.CancellationToken);

        await act.Should().ThrowExactlyAsync<AmbiguousPackageMatchException>().WithMessage("*Contoso.DevKit*Another.Owner*--exact*");
    }

    [Fact]
    public async Task ResolveLatestVersionAsync_PicksHighestStable_WhenPrereleaseDisabled()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_defaultSource, BuildClient(_defaultSource, versions: [V("1.0.0"), V("2.0.0-beta.1"), V("1.5.0")])));

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionAsync(
            "alpha", includePrerelease: false, cancellationToken: TestContext.Current.CancellationToken);

        resolved.Should().NotBeNull();
        resolved!.Version.Should().Be(V("1.5.0"));
        resolved.Source.Name.Should().Be(_defaultSource.Name);
    }

    [Fact]
    public async Task ResolveLatestVersionAsync_IncludesPrerelease_WhenEnabled()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_defaultSource, BuildClient(_defaultSource, versions: [V("1.0.0"), V("2.0.0-beta.1"), V("1.5.0")])));

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionAsync(
            "alpha", includePrerelease: true, cancellationToken: TestContext.Current.CancellationToken);

        resolved!.Version.Should().Be(V("2.0.0-beta.1"));
    }

    [Fact]
    public async Task ResolveLatestVersionAsync_IncludesPrerelease_WhenEnvironmentOverrideEnabled()
    {
        WorkloadCatalog catalog = NewCatalog(
            new WorkloadCatalogOptions { IncludePrerelease = true },
            (_defaultSource, BuildClient(_defaultSource, versions: [V("1.0.0"), V("2.0.0-beta.1"), V("1.5.0")])));

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionAsync(
            "alpha", includePrerelease: null, cancellationToken: TestContext.Current.CancellationToken);

        resolved!.Version.Should().Be(V("2.0.0-beta.1"));
    }

    [Fact]
    public async Task SearchAsync_IncludesPrerelease_WhenEnvironmentOverrideEnabled()
    {
        NuGetProtocolSourceClient client = BuildClient(_defaultSource, search: [("alpha", "2.0.0-beta.1")]);
        WorkloadCatalog catalog = NewCatalog(new WorkloadCatalogOptions { IncludePrerelease = true }, (_defaultSource, client));

        await catalog.SearchAsync(
            new CatalogSearchQuery { IncludePrerelease = null },
            TestContext.Current.CancellationToken);

        var fakeClient = client.Should().BeOfType<FakeClient>().Subject;
        fakeClient.LastSearchUri.Should().NotBeNull();
        fakeClient.LastSearchUri!.Query.Should().ContainEquivalentOf("prerelease=true");
    }

    [Fact]
    public async Task ResolveLatestVersionInRangeAsync_IncludesPrerelease_WhenEnvironmentOverrideEnabled()
    {
        WorkloadCatalog catalog = NewCatalog(
            new WorkloadCatalogOptions { IncludePrerelease = true },
            (_defaultSource, BuildClient(_defaultSource, versions: [V("1.0.0"), V("2.0.0-beta.1"), V("1.5.0")])));
        var range = VersionRange.Parse("[2.0.0-beta.1,2.0.0)");

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionInRangeAsync(
            "alpha", range, includePrerelease: null, cancellationToken: TestContext.Current.CancellationToken);

        resolved!.Version.Should().Be(V("2.0.0-beta.1"));
    }

    [Fact]
    public async Task ResolveLatestVersionInRangeAsync_ExactStableRange_MatchesPrereleaseCandidate_WhenIncludePrereleaseTrue()
    {
        // Regression: the published profile pins node "[3.13.0]" exact but
        // only a "3.13.0-preview.1" candidate exists upstream. NuGet's
        // VersionRange.Satisfies rejects prerelease candidates inside a
        // stable range, so the resolver must apply a prerelease-aware
        // bounds check when IncludePrerelease is on.
        WorkloadCatalog catalog = NewCatalog(
            new WorkloadCatalogOptions { IncludePrerelease = true },
            (_defaultSource, BuildClient(_defaultSource, versions: [V("3.13.0-preview.1")])));
        var range = VersionRange.Parse("[3.13.0]");

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionInRangeAsync(
            "node", range, includePrerelease: null, cancellationToken: TestContext.Current.CancellationToken);

        resolved.Should().NotBeNull();
        resolved!.Version.Should().Be(V("3.13.0-preview.1"));
    }

    [Fact]
    public async Task ResolveLatestVersionInRangeAsync_ExplicitFalse_OverridesOptions()
    {
        WorkloadCatalog catalog = NewCatalog(
            new WorkloadCatalogOptions { IncludePrerelease = true },
            (_defaultSource, BuildClient(_defaultSource, versions: [V("3.13.0-preview.1")])));
        var range = VersionRange.Parse("[3.13.0]");

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionInRangeAsync(
            "node", range, includePrerelease: false, cancellationToken: TestContext.Current.CancellationToken);

        resolved.Should().BeNull();
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("off", false)]
    public void Configure_EnvOverride_WinsBothDirections(string value, bool expected)
    {
        IProcessEnvironment processEnvironment = Substitute.For<IProcessEnvironment>();
        processEnvironment.Get(Constants.WorkloadsSourceEnvironmentVariable).Returns((string?)null);
        processEnvironment.Get(Constants.WorkloadsPrereleaseEnvironmentVariable).Returns(value);
        // CLI version is intentionally prerelease to prove the env value, not auto-detect, wins.
        ICliVersionProvider cliVersionProvider = StubCliVersion("5.0.0-preview.1");
        WorkloadCatalogOptionsSetup setup = new(processEnvironment, cliVersionProvider);
        WorkloadCatalogOptions options = new();

        setup.Configure(options);

        options.IncludePrerelease.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("anything")]
    public void Configure_NoEnvOverride_AutoDetectsFromCliPrerelease(string? value)
    {
        IProcessEnvironment processEnvironment = Substitute.For<IProcessEnvironment>();
        processEnvironment.Get(Constants.WorkloadsSourceEnvironmentVariable).Returns((string?)null);
        processEnvironment.Get(Constants.WorkloadsPrereleaseEnvironmentVariable).Returns(value);
        ICliVersionProvider cliVersionProvider = StubCliVersion("5.0.0-preview.1");
        WorkloadCatalogOptionsSetup setup = new(processEnvironment, cliVersionProvider);
        WorkloadCatalogOptions options = new();

        setup.Configure(options);

        options.IncludePrerelease.Should().BeTrue();
    }

    [Fact]
    public void Configure_StableCli_DefaultsToFalse()
    {
        IProcessEnvironment processEnvironment = Substitute.For<IProcessEnvironment>();
        processEnvironment.Get(Constants.WorkloadsSourceEnvironmentVariable).Returns((string?)null);
        processEnvironment.Get(Constants.WorkloadsPrereleaseEnvironmentVariable).Returns((string?)null);
        ICliVersionProvider cliVersionProvider = StubCliVersion("5.0.0");
        WorkloadCatalogOptionsSetup setup = new(processEnvironment, cliVersionProvider);
        WorkloadCatalogOptions options = new();

        setup.Configure(options);

        options.IncludePrerelease.Should().BeFalse();
    }

    [Fact]
    public void Configure_PrereleaseCli_EnvDisablesOverride()
    {
        IProcessEnvironment processEnvironment = Substitute.For<IProcessEnvironment>();
        processEnvironment.Get(Constants.WorkloadsSourceEnvironmentVariable).Returns((string?)null);
        processEnvironment.Get(Constants.WorkloadsPrereleaseEnvironmentVariable).Returns("false");
        ICliVersionProvider cliVersionProvider = StubCliVersion("5.0.0-preview.1");
        WorkloadCatalogOptionsSetup setup = new(processEnvironment, cliVersionProvider);
        WorkloadCatalogOptions options = new();

        setup.Configure(options);

        options.IncludePrerelease.Should().BeFalse();
    }

    [Fact]
    public void Configure_ResolvesSourceOverride()
    {
        IProcessEnvironment processEnvironment = Substitute.For<IProcessEnvironment>();
        processEnvironment.Get(Constants.WorkloadsSourceEnvironmentVariable).Returns("https://source.test/v3/index.json");
        processEnvironment.Get(Constants.WorkloadsPrereleaseEnvironmentVariable).Returns((string?)null);
        WorkloadCatalogOptionsSetup setup = new(processEnvironment, StubCliVersion("5.0.0"));
        WorkloadCatalogOptions options = new();

        setup.Configure(options);

        options.Source.Should().Be("https://source.test/v3/index.json");
    }

    [Fact]
    public async Task ResolveLatestVersionAsync_ConstrainsToSameMajor_WhenAllowMajorFalse()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_defaultSource, BuildClient(_defaultSource, versions: [V("1.0.0"), V("1.5.0"), V("2.0.0"), V("2.1.0")])));

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionAsync(
            "alpha", includePrerelease: false, currentVersion: V("1.0.0"), allowMajor: false,
            cancellationToken: TestContext.Current.CancellationToken);

        resolved!.Version.Should().Be(V("1.5.0"));
    }

    [Fact]
    public async Task ResolveLatestVersionAsync_NoMatch_ReturnsNull()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_defaultSource, BuildClient(_defaultSource, versions: [V("1.0.0-beta")])));

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionAsync(
            "alpha", includePrerelease: false, cancellationToken: TestContext.Current.CancellationToken);

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task ResolveLatestVersionInRangeAsync_PicksHighestStableInsideRange()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_defaultSource, BuildClient(_defaultSource, versions: [V("4.900.0"), V("4.1048.199"), V("4.1048.200"), V("4.1100.0")])));

        var range = VersionRange.Parse("[1.8.1, 4.1048.200)");

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionInRangeAsync(
            "alpha", range, includePrerelease: false, cancellationToken: TestContext.Current.CancellationToken);

        resolved.Should().NotBeNull();
        resolved!.Version.Should().Be(V("4.1048.199"));
        resolved.Source.Name.Should().Be(_defaultSource.Name);
    }

    [Fact]
    public async Task ResolveLatestVersionOnChannelAsync_StableChannel_ExcludesPrerelease()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_defaultSource, BuildClient(_defaultSource, versions: [V("1.0.0"), V("2.0.0-preview.1"), V("1.5.0")])));

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionOnChannelAsync(
            "alpha", prereleaseLabel: null, cancellationToken: TestContext.Current.CancellationToken);

        resolved.Should().NotBeNull();
        resolved!.Version.Should().Be(V("1.5.0"));
    }

    [Fact]
    public async Task ResolveLatestVersionOnChannelAsync_PreviewChannel_PicksHighestPreview()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_defaultSource, BuildClient(_defaultSource, versions:
                [V("1.0.0"), V("2.0.0-preview.1"), V("2.0.0-preview.3"), V("2.0.0-experimental.1")])));

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionOnChannelAsync(
            "alpha", prereleaseLabel: "preview", cancellationToken: TestContext.Current.CancellationToken);

        resolved.Should().NotBeNull();
        resolved!.Version.Should().Be(V("2.0.0-preview.3"));
    }

    [Fact]
    public async Task ResolveLatestVersionOnChannelAsync_ExperimentalChannel_PicksExperimental()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_defaultSource, BuildClient(_defaultSource, versions:
                [V("1.0.0"), V("2.0.0-preview.1"), V("2.5.0-experimental.2")])));

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionOnChannelAsync(
            "alpha", prereleaseLabel: "experimental", cancellationToken: TestContext.Current.CancellationToken);

        resolved.Should().NotBeNull();
        resolved!.Version.Should().Be(V("2.5.0-experimental.2"));
    }

    [Fact]
    public async Task ResolveLatestVersionOnChannelAsync_PreviewChannel_NoPreviewPublished_ReturnsNull()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_defaultSource, BuildClient(_defaultSource, versions: [V("1.0.0"), V("2.0.0")])));

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionOnChannelAsync(
            "alpha", prereleaseLabel: "preview", cancellationToken: TestContext.Current.CancellationToken);

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task ResolveLatestVersionOnChannelAsync_PreviewChannel_RespectsRange()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_defaultSource, BuildClient(_defaultSource, versions: [V("4.10.0-preview.1"), V("4.20.0-preview.1")])));
        var range = VersionRange.Parse("[4.0.0, 4.15.0)");

        ResolvedPackage? resolved = await catalog.ResolveLatestVersionOnChannelAsync(
            "alpha", prereleaseLabel: "preview", versionRange: range,
            cancellationToken: TestContext.Current.CancellationToken);

        resolved.Should().NotBeNull();
        resolved!.Version.Should().Be(V("4.10.0-preview.1"));
    }

    [Fact]
    public async Task ResolveVersionAsync_ReturnsResolvedPackage_WhenSourceHasVersion()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_defaultSource, BuildClient(_defaultSource, versions: [V("1.0.0"), V("2.0.0")])));

        ResolvedPackage? resolved = await catalog.ResolveVersionAsync(
            "alpha", V("2.0.0"), cancellationToken: TestContext.Current.CancellationToken);

        resolved.Should().NotBeNull();
        resolved!.Version.Should().Be(V("2.0.0"));
        resolved.Source.Name.Should().Be(_defaultSource.Name);
    }

    [Fact]
    public async Task ResolveVersionAsync_VersionMissing_ReturnsNull()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_defaultSource, BuildClient(_defaultSource, versions: [V("1.0.0")])));

        ResolvedPackage? resolved = await catalog.ResolveVersionAsync(
            "alpha", V("9.9.9"), cancellationToken: TestContext.Current.CancellationToken);

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task ResolveVersionAsync_PreservesPackageId()
    {
        WorkloadCatalog catalog = NewCatalog(
            (_defaultSource, BuildClient(_defaultSource, versions: [V("1.0.0")])));

        ResolvedPackage? resolved = await catalog.ResolveVersionAsync(
            "Alpha", V("1.0.0"), cancellationToken: TestContext.Current.CancellationToken);

        resolved!.PackageId.Should().Be("Alpha");
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

        await using Stream result = await catalog.DownloadAsync(
            new ResolvedPackage("alpha", V("1.0.0"), _defaultSource), TestContext.Current.CancellationToken);

        var copied = new MemoryStream();
        await result.CopyToAsync(copied, TestContext.Current.CancellationToken);
        copied.ToArray().Should().Equal(payload);
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

        await FluentActions.Awaiting(() => catalog.DownloadAsync(new ResolvedPackage("alpha", V("1.0.0"), _defaultSource))).Should().ThrowAsync<WorkloadPackageNotFoundException>();
    }

    private static NuGetVersion V(string v) => NuGetVersion.Parse(v);

    private sealed class FakeClient(SourceRepository repo, JObject? response, Func<Uri, JObject?>? getPage = null) : NuGetProtocolSourceClient(repo)
    {
        public Uri? LastSearchUri { get; private set; }

        internal override Task<JObject?> FetchSearchResponseAsync(Uri searchUri, CancellationToken cancellationToken)
        {
            LastSearchUri = searchUri;
            return Task.FromResult(getPage is null ? response : getPage(searchUri));
        }
    }

    private static JObject SearchResponse(params (string Id, string Version)[] hits)
    {
        var data = new JArray();
        foreach ((string id, string version) in hits)
        {
            data.Add(new JObject
            {
                ["id"] = id,
                ["version"] = version,
            });
        }

        return new JObject { ["data"] = data, ["totalHits"] = hits.Length };
    }

    private static ServiceIndexResourceV3 NewServiceIndex()
    {
        // Minimal V3 service index that advertises a SearchQueryService URL.
        // The URL itself is irrelevant since FakeClient short-circuits the
        // actual HTTP fetch; what matters is that TryBuildV3SearchUriAsync
        // resolves a non-null URI and routes through FetchSearchResponseAsync.
        var json = JObject.Parse("""
            {
              "version": "3.0.0",
              "resources": [
                { "@id": "https://search.test/query", "@type": "SearchQueryService" }
              ]
            }
            """);
        return new ServiceIndexResourceV3(json, DateTime.UtcNow);
    }

    private static NuGetProtocolSourceClient BuildClient(
        PackageSource source,
        (string Id, string Version)[]? search = null,
        IEnumerable<NuGetVersion>? versions = null)
    {
        FindPackageByIdResource? findResource = null;
        if (versions is not null)
        {
            findResource = Substitute.For<FindPackageByIdResource>();
            findResource.GetAllVersionsAsync(Arg.Any<string>(), Arg.Any<SourceCacheContext>(), Arg.Any<ILogger>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(versions));
        }

        SourceRepository repo = TestRepository.Build(source, NewServiceIndex(), findResource);
        return new FakeClient(repo, search is null ? null : SearchResponse(search));
    }

    private static WorkloadCatalog NewCatalog(params (PackageSource Source, NuGetProtocolSourceClient Client)[] entries)
        => NewCatalog(new WorkloadCatalogOptions(), entries);

    private static WorkloadCatalog NewCatalog(WorkloadCatalogOptions options, params (PackageSource Source, NuGetProtocolSourceClient Client)[] entries)
    {
        var sourceProvider = Substitute.For<IPackageSourceProvider>();
        sourceProvider.GetSource(Arg.Any<string?>()).Returns(entries[0].Source);

        var byName = entries.ToDictionary(e => e.Source.Name, e => e.Client, StringComparer.OrdinalIgnoreCase);
        return new WorkloadCatalog(Options.Create(options), sourceProvider, source => byName[source.Name]);
    }

    private static ICliVersionProvider StubCliVersion(string informational)
    {
        ICliVersionProvider provider = Substitute.For<ICliVersionProvider>();
        provider.InformationalVersion.Returns(informational);
        provider.Version.Returns(informational.Split('-', '+')[0]);
        provider.IsPrerelease.Returns(informational.Contains('-'));
        return provider;
    }
}
