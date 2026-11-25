// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Packages;
using Azure.Functions.Cli.Tests.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.Extensions.Options;
using NSubstitute;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates.Packages;

public sealed class TemplateCatalogTests
{
    [Fact]
    public void Options_UseOnlyTemplateSource()
    {
        var environment = Substitute.For<IProcessEnvironment>();
        environment.Get("FUNC_CLI_TEMPLATES_SOURCE").Returns("https://templates.test/v3/index.json");
        environment.Get("FUNC_CLI_WORKLOADS_SOURCE").Returns("https://workloads.test/v3/index.json");
        environment.Get("FUNC_CLI_WORKLOADS_PRERELEASE").Returns("true");
        var options = new TemplateCatalogOptions();

        new TemplateCatalogOptionsSetup(environment).Configure(options);

        options.Source.Should().Be("https://templates.test/v3/index.json");
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
        var versions = Metadata(("1.0.0", true), ("2.0.0-preview.1", true), ("3.0.0", false), ("4.0.0-preview.1", false));
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()),
            source => new NuGetProtocolSourceClient(TestRepository.Build(source, versions)));

        var result = await catalog.ResolveVersionAsync("test.templates", null, "https://feed.test/index.json", prerelease, CancellationToken.None);

        result.ToNormalizedString().Should().Be(expected);
        await versions.Received(1).GetMetadataAsync("test.templates", true, false, Arg.Is<SourceCacheContext>(cache => cache.NoCache),
            Arg.Any<NuGet.Common.ILogger>(), Arg.Any<CancellationToken>());
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
            var metadata = Metadata(("2.0.0", true));
            resource.CopyNupkgToStreamAsync(
                "test.templates", version, Arg.Any<Stream>(), Arg.Any<SourceCacheContext>(),
                Arg.Any<NuGet.Common.ILogger>(), Arg.Any<CancellationToken>()).Returns(async call =>
                {
                    call.ArgAt<SourceCacheContext>(3).NoCache.Should().BeFalse();
                    await call.ArgAt<Stream>(2).WriteAsync(payload, call.ArgAt<CancellationToken>(5));
                    return true;
                });
            return new NuGetProtocolSourceClient(TestRepository.Build(source, resource, metadata));
        });

        var resolved = await catalog.ResolveVersionAsync("test.templates", null, explicitSource, false, TestContext.Current.CancellationToken);
        await using Stream archive = await catalog.OpenPackageAsync("test.templates", resolved, explicitSource, TestContext.Current.CancellationToken);

        using var bytes = new MemoryStream();
        await archive.CopyToAsync(bytes, TestContext.Current.CancellationToken);
        bytes.ToArray().Should().Equal(payload);
        archive.CanSeek.Should().BeTrue();
        resolved.Should().Be(version);
        requestedSources.Should().Equal(expected, expected);
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
        await resource.Received(2).CopyNupkgToStreamAsync(Arg.Any<string>(), Arg.Any<NuGetVersion>(), Arg.Any<Stream>(),
            Arg.Any<SourceCacheContext>(), Arg.Any<NuGet.Common.ILogger>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Install_StaleVersionIndexRetriesWithoutCache(bool exactVersion)
    {
        byte[] payload = [1, 2, 3];
        var version = NuGetVersion.Parse("2.0.0");
        var metadata = Metadata(("2.0.0", true));
        var resource = Substitute.For<FindPackageByIdResource>();
        List<bool> cacheModes = [];
        resource.CopyNupkgToStreamAsync(
            "test.templates", version, Arg.Any<Stream>(), Arg.Any<SourceCacheContext>(),
            Arg.Any<NuGet.Common.ILogger>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                bool noCache = call.ArgAt<SourceCacheContext>(3).NoCache;
                cacheModes.Add(noCache);
                if (!noCache)
                {
                    return false;
                }

                await call.ArgAt<Stream>(2).WriteAsync(payload, call.ArgAt<CancellationToken>(5));
                return true;
            });
        List<string> sources = [];
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()), source =>
        {
            sources.Add(source.Source);
            return new NuGetProtocolSourceClient(TestRepository.Build(source, metadata, resource));
        });

        var resolved = await catalog.ResolveVersionAsync("test.templates", exactVersion ? "2.0.0" : null,
            "https://feed.test/index.json", false, TestContext.Current.CancellationToken);
        await using Stream archive = await catalog.OpenPackageAsync(
            "test.templates", resolved, "https://feed.test/index.json", TestContext.Current.CancellationToken);

        using var bytes = new MemoryStream();
        await archive.CopyToAsync(bytes, TestContext.Current.CancellationToken);
        bytes.ToArray().Should().Equal(payload);
        cacheModes.Should().Equal(false, true);
        sources.Should().HaveCount(exactVersion ? 2 : 3).And.OnlyContain(source => source == "https://feed.test/index.json");
        await metadata.Received(exactVersion ? 0 : 1).GetMetadataAsync(
            "test.templates", true, false, Arg.Is<SourceCacheContext>(cache => cache.NoCache),
            Arg.Any<NuGet.Common.ILogger>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenPackage_RetryFeedFailureIsNotReportedAsMissing()
    {
        var resource = Substitute.For<FindPackageByIdResource>();
        resource.CopyNupkgToStreamAsync(Arg.Any<string>(), Arg.Any<NuGetVersion>(), Arg.Any<Stream>(), Arg.Any<SourceCacheContext>(),
            Arg.Any<NuGet.Common.ILogger>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false), Task.FromException<bool>(new FatalProtocolException("feed unavailable")));
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()),
            source => new NuGetProtocolSourceClient(TestRepository.Build(source, resource)));

        Func<Task> act = () => catalog.OpenPackageAsync("test.templates", NuGetVersion.Parse("1.0.0"), null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*Cannot read template feed*");
    }

    [Fact]
    public async Task OpenPackage_CancellationAfterCacheMissDoesNotRetry()
    {
        using var cancellation = new CancellationTokenSource();
        var resource = Substitute.For<FindPackageByIdResource>();
        resource.CopyNupkgToStreamAsync(Arg.Any<string>(), Arg.Any<NuGetVersion>(), Arg.Any<Stream>(), Arg.Any<SourceCacheContext>(),
            Arg.Any<NuGet.Common.ILogger>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                cancellation.Cancel();
                return false;
            });
        int clients = 0;
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()), source =>
        {
            clients++;
            return new NuGetProtocolSourceClient(TestRepository.Build(source, resource));
        });

        Func<Task> act = () => catalog.OpenPackageAsync("test.templates", NuGetVersion.Parse("1.0.0"), null, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        clients.Should().Be(1);
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
    public async Task ResolveVersion_DefaultRejectsAPrereleaseOnlyFeed()
    {
        var resource = Metadata(("1.0.0-preview.1", true));
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
        var metadata = Substitute.For<PackageMetadataResource>();
        metadata.GetMetadataAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<SourceCacheContext>(),
            Arg.Any<NuGet.Common.ILogger>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IEnumerable<IPackageSearchMetadata>>(new FatalProtocolException("feed unavailable")));
        resource.CopyNupkgToStreamAsync(Arg.Any<string>(), Arg.Any<NuGetVersion>(), Arg.Any<Stream>(), Arg.Any<SourceCacheContext>(),
            Arg.Any<NuGet.Common.ILogger>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<bool>(new FatalProtocolException("feed unavailable")));
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions { Source = "https://env.test/index.json" }), source =>
        {
            requests++;
            source.Source.Should().Be("https://explicit.test/index.json");
            return new NuGetProtocolSourceClient(TestRepository.Build(source, resource, metadata));
        });

        Func<Task> act = () => download
            ? catalog.OpenPackageAsync("test.templates", NuGetVersion.Parse("1.0.0"), "https://explicit.test/index.json", TestContext.Current.CancellationToken)
            : catalog.ResolveVersionAsync("test.templates", null, "https://explicit.test/index.json", false, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*Cannot read template feed*explicit.test*");
        requests.Should().Be(1);
    }

    [Fact]
    public async Task ResolveVersion_FeedFailureRedactsCredentialsAndQueryFromDiagnostic()
    {
        const string source = "https://user:password@feed.test/v3/index.json?token=sensitive";
        const string nestedUrl = "https://user:password@feed.test/v3/registration?token=sensitive";
        var metadata = Substitute.For<PackageMetadataResource>();
        metadata.GetMetadataAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<SourceCacheContext>(),
            Arg.Any<NuGet.Common.ILogger>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IEnumerable<IPackageSearchMetadata>>(
                new FatalProtocolException($"Request to {nestedUrl} failed.")));
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions { Source = source }),
            feed => new NuGetProtocolSourceClient(TestRepository.Build(feed, metadata)));

        Func<Task> act = () => catalog.ResolveVersionAsync("test.templates", null, null, false, TestContext.Current.CancellationToken);

        TemplatePackageException exception = (await act.Should().ThrowAsync<TemplatePackageException>()).Which;
        exception.Message.Should().Contain("https://feed.test/v3/index.json");
        exception.ToString().Should().NotContain("user:password").And.NotContain("sensitive");
    }

    [Fact]
    public void SourceValidationFailureRedactsCredentialsAndQuery()
    {
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()),
            _ => throw new InvalidOperationException());

        Action act = () => catalog.GetSource("ftp://user:password@feed.test/index.json?token=sensitive");

        var exception = act.Should().Throw<ArgumentException>().Which;
        exception.Message.Should().NotContain("user:password").And.NotContain("sensitive");
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

        Func<Task> resolve = () => catalog.ResolveVersionAsync("test.templates", null, explicitSource, false, TestContext.Current.CancellationToken);
        Func<Task> exact = () => catalog.ResolveVersionAsync("test.templates", "1.0.0", explicitSource, false, TestContext.Current.CancellationToken);
        Func<Task> download = () => catalog.OpenPackageAsync("test.templates", NuGetVersion.Parse("1.0.0"), explicitSource,
            TestContext.Current.CancellationToken);

        await resolve.Should().ThrowAsync<ArgumentException>();
        await exact.Should().ThrowAsync<ArgumentException>();
        await download.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ResolveVersion_OnlyUnlistedVersionsReportsNoMatch()
    {
        var metadata = Metadata(("1.0.0", false), ("2.0.0-preview.1", false));
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()),
            source => new NuGetProtocolSourceClient(TestRepository.Build(source, metadata)));

        Func<Task> act = () => catalog.ResolveVersionAsync("test.templates", null, null, true, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<TemplatePackageException>().WithMessage("*No matching version*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CatalogOperations_MissingResourceReportsFeedFailure(bool download)
    {
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()),
            source => new NuGetProtocolSourceClient(TestRepository.Build(source)));

        Func<Task> act = () => download
            ? catalog.OpenPackageAsync("test.templates", NuGetVersion.Parse("1.0.0"), null, TestContext.Current.CancellationToken)
            : catalog.ResolveVersionAsync("test.templates", null, null, false, TestContext.Current.CancellationToken);

        var exception = await act.Should().ThrowAsync<TemplatePackageException>();
        exception.WithMessage("*required V3 service*").WithInnerException<NuGetResourceUnavailableException>();
    }

    [Fact]
    public async Task ResolveVersion_UnexpectedInvalidOperationIsNotMisreportedAsMissingResource()
    {
        var resource = Substitute.For<PackageMetadataResource>();
        var failure = new InvalidOperationException("unexpected failure");
        resource.GetMetadataAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<SourceCacheContext>(),
            Arg.Any<NuGet.Common.ILogger>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IEnumerable<IPackageSearchMetadata>>(failure));
        var catalog = new TemplateCatalog(Options.Create(new TemplateCatalogOptions()),
            source => new NuGetProtocolSourceClient(TestRepository.Build(source, resource)));

        Func<Task> act = () => catalog.ResolveVersionAsync("test.templates", null, null, false, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
    }

    private static PackageMetadataResource Metadata(params (string Version, bool Listed)[] versions)
    {
        var resource = Substitute.For<PackageMetadataResource>();
        var results = versions.Select(version =>
        {
            var metadata = Substitute.For<IPackageSearchMetadata>();
            metadata.Identity.Returns(new PackageIdentity("test.templates", NuGetVersion.Parse(version.Version)));
            metadata.IsListed.Returns(version.Listed);
            return metadata;
        }).ToList();
        resource.GetMetadataAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<SourceCacheContext>(),
            Arg.Any<NuGet.Common.ILogger>(), Arg.Any<CancellationToken>()).Returns(results);
        return resource;
    }
}
