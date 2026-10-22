// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Commands.Quickstart;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Quickstart;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Quickstart;

public sealed class QuickstartCatalogTests
{
    private const string ManifestUrl = "https://cdn.functions.azure.com/test/manifest.json";
    private const string Reference = """
        {"schemaVersion":1,"packageId":"Example.Templates","version":"1.2.3",
         "source":"https://example.test/v3/index.json","templateIdentity":"Example.Project"}
        """;

    [Theory]
    [InlineData(null, QuickstartPackageMetadataStatus.Absent)]
    [InlineData("null", QuickstartPackageMetadataStatus.Null)]
    [InlineData("\"\"", QuickstartPackageMetadataStatus.Invalid)]
    [InlineData("[]", QuickstartPackageMetadataStatus.Invalid)]
    [InlineData("false", QuickstartPackageMetadataStatus.Invalid)]
    [InlineData("{}", QuickstartPackageMetadataStatus.Invalid)]
    [InlineData("{\"schemaVersion\":\"1\"}", QuickstartPackageMetadataStatus.Invalid)]
    [InlineData("{\"schemaVersion\":null}", QuickstartPackageMetadataStatus.Invalid)]
    [InlineData("{\"schemaVersion\":0}", QuickstartPackageMetadataStatus.Invalid)]
    [InlineData("{\"schemaVersion\":1.5}", QuickstartPackageMetadataStatus.Invalid)]
    [InlineData("{\"schemaVersion\":2,\"future\":true}", QuickstartPackageMetadataStatus.Unsupported)]
    [InlineData("{\"schemaVersion\":1,\"SCHEMAVERSION\":2}", QuickstartPackageMetadataStatus.Invalid)]
    public async Task SearchAsync_OptionalMetadata_PreservesLegacyEntry(string? metadata, object expected)
    {
        using Fixture fixture = new(Manifest(Entry("legacy", metadata)));

        QuickstartCatalog result = await fixture.Catalog.SearchAsync(new(), CancellationToken.None);
        QuickstartManifest legacy = await fixture.Source.GetManifestAsync();

        result.Entries.Should().ContainSingle().Which.MetadataStatus.Should().Be((QuickstartPackageMetadataStatus)expected);
        result.Entries[0].PackageReference.Should().BeNull();
        legacy.Entries.Should().ContainSingle().Which.Id.Should().Be("legacy");
        fixture.Handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("packageId", "\"bad package\"")]
    [InlineData("packageId", "null")]
    [InlineData("version", "\"[1.0.0,2.0.0)\"")]
    [InlineData("version", "\"\"")]
    [InlineData("version", "123")]
    [InlineData("source", "\"relative/index.json\"")]
    [InlineData("source", "\"http://example.test/v3/index.json\"")]
    [InlineData("source", "\"https://user:secret@example.test/index.json\"")]
    [InlineData("source", "\"https://example.test/index.json#fragment\"")]
    [InlineData("templateIdentity", "\" \"")]
    [InlineData("groupIdentity", "null")]
    [InlineData("approved", "true")]
    [InlineData("artifact", "null")]
    [InlineData("artifact", "{}")]
    [InlineData("artifact", "{\"algorithm\":\"sha256\",\"digest\":\"abc\",\"evidenceReference\":\"review\"}")]
    public async Task SearchAsync_InvalidReference_ReportsDiagnosticWithoutBreakingLegacy(string property, string value)
    {
        JsonObject reference = JsonNode.Parse(Reference)!.AsObject();
        reference[property] = JsonNode.Parse(value);
        using Fixture fixture = new(Manifest(Entry("legacy", reference.ToJsonString())));

        QuickstartCatalog result = await fixture.Catalog.SearchAsync(new(), CancellationToken.None);

        result.Entries[0].MetadataStatus.Should().Be(QuickstartPackageMetadataStatus.Invalid);
        result.Entries[0].MetadataDiagnostic.Should().NotBeNullOrWhiteSpace();
        result.Entries[0].PackageReference.Should().BeNull();
        (await fixture.Source.GetManifestAsync()).Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task SearchAsync_AdvertisedArtifact_RemainsUnverifiedAndDoesNotFetchEvidence()
    {
        const string metadata = """
            {"schemaVersion":1,"packageId":"Example.Templates","version":"1.2.3",
             "source":"https://example.test/v3/index.json","templateIdentity":"Example.Project","groupIdentity":"Example.Group",
             "artifact":{"algorithm":"sha256",
              "digest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "evidenceReference":"https://evidence.example.test/approval"},
             "projects":[{"root":"src/__name__","stack":"python","conditional":true}],
             "requiredWorkloads":{"unagreedSummary":"opaque"}}
            """;
        using Fixture fixture = new(Manifest(Entry("sample", metadata)), origin: QuickstartCatalogOrigin.Download);

        QuickstartCatalog result = await fixture.Catalog.SearchAsync(new(), CancellationToken.None);

        QuickstartCatalogEntry entry = result.Entries.Should().ContainSingle().Subject;
        entry.MetadataStatus.Should().Be(QuickstartPackageMetadataStatus.Unverified);
        entry.MetadataDiagnostic.Should().Contain("have not been verified");
        QuickstartPackageReference reference = entry.PackageReference!;
        reference.PackageId.Should().Be("Example.Templates");
        reference.Version.Should().Be("1.2.3");
        reference.Source.Should().Be("https://example.test/v3/index.json");
        reference.TemplateIdentity.Should().Be("Example.Project");
        reference.GroupIdentity.Should().Be("Example.Group");
        reference.Artifact!.EvidenceReference.Should().Be("https://evidence.example.test/approval");
        reference.Projects[0].GetProperty("conditional").GetBoolean().Should().BeTrue();
        reference.RequiredWorkloads.GetProperty("unagreedSummary").GetString().Should().Be("opaque");
        fixture.Handler.Requests.Should().Equal(ManifestUrl);
        fixture.Cache.Received(1).WriteManifest(Arg.Any<string>());
    }

    [Fact]
    public async Task SearchAsync_DuplicatePackageReference_RejectsMetadataOnly()
    {
        string entry = Entry("duplicate", Reference).Replace("\"priority\":100",
            "\"priority\":100,\"PACKAGEREFERENCE\":null", StringComparison.Ordinal);
        using Fixture fixture = new(Manifest(entry));

        QuickstartCatalog result = await fixture.Catalog.SearchAsync(new(), CancellationToken.None);

        result.Entries[0].MetadataStatus.Should().Be(QuickstartPackageMetadataStatus.Invalid);
        result.Entries[0].MetadataDiagnostic.Should().Contain("duplicated");
        (await fixture.Source.GetManifestAsync()).Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task SearchAsync_DuplicateIds_ReportsAllWithoutChangingLegacyRows()
    {
        using Fixture fixture = new(Manifest(Entry("same", Reference), Entry("SAME", null)));

        QuickstartCatalog result = await fixture.Catalog.SearchAsync(new(), CancellationToken.None);

        result.Entries.Should().HaveCount(2).And.OnlyContain(entry => entry.MetadataStatus == QuickstartPackageMetadataStatus.Conflicting);
        (await fixture.Source.GetManifestAsync()).Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchAsync_CompetingOwners_AreDetectedBeforeFiltering()
    {
        using Fixture fixture = new(Manifest(
            Entry("one", Reference, language: "Python"),
            Entry("two", Reference.Replace("Example.Templates", "Other.Templates", StringComparison.Ordinal), language: "JavaScript")));

        QuickstartCatalog result = await fixture.Catalog.SearchAsync(new(Language: "Python"), CancellationToken.None);

        result.Entries.Should().ContainSingle().Which.MetadataStatus.Should().Be(QuickstartPackageMetadataStatus.Conflicting);
    }

    [Fact]
    public async Task SearchAsync_CompetingArtifactSources_AreNotSilentlySelected()
    {
        using Fixture fixture = new(Manifest(
            Entry("one", Reference),
            Entry("two", Reference.Replace("example.test", "other.test", StringComparison.Ordinal))));

        QuickstartCatalog result = await fixture.Catalog.SearchAsync(new(), CancellationToken.None);

        result.Entries.Should().HaveCount(2).And.OnlyContain(entry => entry.MetadataStatus == QuickstartPackageMetadataStatus.Conflicting);
    }

    [Fact]
    public async Task SearchAsync_CompetingArtifactDigests_AreNotSilentlySelected()
    {
        JsonObject first = JsonNode.Parse(Reference)!.AsObject();
        first["artifact"] = new JsonObject
        {
            ["algorithm"] = "sha256",
            ["digest"] = new string('a', 64),
            ["evidenceReference"] = "inert-evidence",
        };
        JsonObject second = first.DeepClone().AsObject();
        second["artifact"]!["digest"] = new string('b', 64);
        using Fixture fixture = new(Manifest(Entry("one", first.ToJsonString()), Entry("two", second.ToJsonString())));

        QuickstartCatalog result = await fixture.Catalog.SearchAsync(new(), CancellationToken.None);

        result.Entries.Should().HaveCount(2).And.OnlyContain(entry => entry.MetadataStatus == QuickstartPackageMetadataStatus.Conflicting);
    }

    [Fact]
    public async Task SearchAsync_CaseInsensitiveMetadata_DoesNotChangeAdvertisedReference()
    {
        string metadata = Reference.Replace("schemaVersion", "SCHEMAVERSION", StringComparison.Ordinal)
            .Replace("packageId", "PACKAGEID", StringComparison.Ordinal);
        using Fixture fixture = new(Manifest(Entry("sample", metadata)));

        QuickstartCatalog result = await fixture.Catalog.SearchAsync(new(), CancellationToken.None);

        result.Entries[0].MetadataStatus.Should().Be(QuickstartPackageMetadataStatus.Unverified);
        result.Entries[0].PackageReference!.PackageId.Should().Be("Example.Templates");
    }

    [Fact]
    public async Task SearchAsync_SameDisplayNameWithDifferentIdentities_DoesNotInferOwnership()
    {
        using Fixture fixture = new(Manifest(
            Entry("one", Reference),
            Entry("two", Reference.Replace("Example.Templates", "Other.Templates", StringComparison.Ordinal)
                .Replace("Example.Project", "Other.Project", StringComparison.Ordinal))));

        QuickstartCatalog result = await fixture.Catalog.SearchAsync(new(), CancellationToken.None);

        result.Entries.Should().HaveCount(2).And.OnlyContain(entry => entry.MetadataStatus == QuickstartPackageMetadataStatus.Unverified);
    }

    [Fact]
    public async Task SearchAsync_FiltersAndOrdering_PreserveLegacySemantics()
    {
        using Fixture fixture = new(Manifest(
            Entry("third", Reference, priority: 300),
            Entry("first", null, priority: 10),
            Entry("second", null, priority: 10),
            Entry("wrong-language", Reference, language: "JavaScript"),
            Entry("wrong-resource", null, resource: "timer"),
            Entry("wrong-iac", null, iac: "terraform")));

        QuickstartCatalog result = await fixture.Catalog.SearchAsync(
            new(Language: "python", Resource: "HTTP", Iac: "BICEP", Search: "sample"), CancellationToken.None);

        result.Entries.Select(entry => entry.Entry.Id).Should().Equal("first", "second", "third");
        QuickstartCatalog all = await fixture.Catalog.SearchAsync(new(), CancellationToken.None);
        all.Entries.Should().HaveCount(6);
        (await fixture.Catalog.SearchAsync(new(Search: "not-present"), CancellationToken.None)).Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_InvalidLegacySource_DoesNotBecomeValidThroughPackageMetadata()
    {
        string entry = Entry("untrusted", Reference).Replace("Azure-Samples/test-repo", "unknown/test-repo", StringComparison.Ordinal);
        using Fixture fixture = new(Manifest(entry));

        (await fixture.Catalog.SearchAsync(new(), CancellationToken.None)).Entries.Should().BeEmpty();
        (await fixture.Source.GetManifestAsync()).Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyCommands_EnrichedManifest_PreservesJsonShape(bool info)
    {
        string entry = Entry("sample", Reference).Replace("\"priority\":100",
            "\"priority\":100,\"shortDescription\":\"Short\",\"longDescription\":\"Long\",\"whatsIncluded\":[\"Included\"]",
            StringComparison.Ordinal);
        using Fixture fixture = new(Manifest(entry));
        TestInteractionService interaction = new();
        IQuickstartProviderResolver resolver = Substitute.For<IQuickstartProviderResolver>();
        IQuickstartProvider provider = Substitute.For<IQuickstartProvider>();
        provider.Stack.Returns("python");
        provider.DisplayName.Returns("Python");
        provider.ManifestLanguages.Returns(["Python"]);
        provider.GetDisplayLanguage("Python").Returns("Python");
        resolver.SelectProviderAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(provider);
        resolver.ResolveOrPromptLanguageAsync(Arg.Any<string?>(), provider, Arg.Any<QuickstartManifest>(), Arg.Any<CancellationToken>())
            .Returns(("Python", (int?)null));
        resolver.FindProviderForLanguage("Python").Returns(provider);
        RootCommand root = new()
        {
            Subcommands =
            {
                info ? new QuickstartInfoCommand(interaction, resolver, fixture.Source)
                    : new QuickstartListCommand(interaction, resolver, fixture.Source, [provider]),
            },
        };

        int exit = await root.Parse(info ? ["info", "sample", "--json"] : ["list", "--json"]).InvokeAsync();

        exit.Should().Be(0);
        string output = interaction.Lines.Single(line => line.StartsWith("JSON:", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(output["JSON:".Length..]);
        JsonElement row = info ? json.RootElement : json.RootElement[0];
        string[] expected = info
            ? ["id", "name", "language", "resource", "iac", "gitRef", "repositoryUrl", "folderPath", "shortDescription", "longDescription", "whatsIncluded"]
            : ["id", "name", "description"];
        row.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(expected);
        row.GetProperty("id").GetString().Should().Be("sample");
        if (info)
        {
            row.GetProperty("shortDescription").GetString().Should().Be("Short");
            row.GetProperty("longDescription").GetString().Should().Be("Long");
            row.GetProperty("whatsIncluded")[0].GetString().Should().Be("Included");
        }
    }

    [Fact]
    public async Task SearchAsync_LegacyStack_UsesCapabilitiesAndExistingLanguageBridge()
    {
        IProjectStack stack = CreateStack();
        IQuickstartProvider provider = Substitute.For<IQuickstartProvider>();
        provider.Stack.Returns("dotnet");
        provider.ManifestLanguages.Returns(["CSharp"]);
        provider.GetDisplayLanguage("CSharp").Returns("C#");
        using Fixture fixture = new(Manifest(Entry("dotnet", null, language: "CSharp"), Entry("python", Reference)));

        QuickstartCatalog result = await fixture.Catalog.SearchAsync(
            new(LegacyStack: stack, LegacyLanguageProvider: provider), CancellationToken.None);

        result.Entries.Select(entry => entry.Entry.Id).Should().Equal("dotnet");
        (await fixture.Catalog.SearchAsync(new(), CancellationToken.None)).Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchAsync_MismatchedLegacyProvider_RejectsBeforeReadingMetadata()
    {
        IQuickstartProvider provider = Substitute.For<IQuickstartProvider>();
        provider.Stack.Returns("python");
        using Fixture fixture = new(Manifest(Entry("sample", Reference)));

        await FluentActions.Awaiting(() => fixture.Catalog.SearchAsync(
            new(LegacyStack: CreateStack(), LegacyLanguageProvider: provider), CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();

        fixture.Cache.DidNotReceive().EnsureDirectory();
    }

    [Fact]
    public async Task SearchAsync_UnknownLegacyLanguage_DoesNotInferItFromPackageId()
    {
        using Fixture fixture = new(Manifest(Entry("unknown", Reference, language: "Unknown")));

        QuickstartCatalog result = await fixture.Catalog.SearchAsync(new(LegacyStack: CreateStack()), CancellationToken.None);

        result.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_AmbiguousStackCapabilities_RejectsBeforeReadingMetadata()
    {
        IProjectStack stack = CreateStack();
        stack.SupportedLanguages.Returns(["C#", "F#"]);
        stack.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>>
        {
            ["C#"] = ["shared"],
            ["F#"] = ["shared"],
        });
        using Fixture fixture = new(Manifest(Entry("sample", Reference)));

        await FluentActions.Awaiting(() => fixture.Catalog.SearchAsync(new(LegacyStack: stack), CancellationToken.None))
            .Should().ThrowAsync<ConflictingProjectStackMetadataException>();
        fixture.Cache.DidNotReceive().EnsureDirectory();
    }

    [Fact]
    public async Task SearchAsync_CanceledBeforeLoad_DoesNotTouchCache()
    {
        using Fixture fixture = new(Manifest(Entry("sample", Reference)));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await FluentActions.Awaiting(() => fixture.Catalog.SearchAsync(new(), cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        fixture.Cache.DidNotReceive().EnsureDirectory();
    }

    [Fact]
    public async Task SearchAsync_CallerCancellation_DoesNotReturnStaleCache()
    {
        using CancellationTokenSource cancellation = new();
        using Fixture fixture = new(Manifest(Entry("cached", Reference)), origin: QuickstartCatalogOrigin.StaleCache);
        fixture.Handler.BeforeResponse = () =>
        {
            cancellation.Cancel();
            throw new TaskCanceledException();
        };

        await FluentActions.Awaiting(() => fixture.Catalog.SearchAsync(new(), cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        fixture.Cache.DidNotReceive().TryReadManifest();
    }

    [Fact]
    public async Task SearchAsync_RequestTimeout_CanReturnStaleMetadataWithoutApproval()
    {
        using Fixture fixture = new(Manifest(Entry("cached", Reference)), origin: QuickstartCatalogOrigin.StaleCache);
        fixture.Handler.BeforeResponse = () => throw new TaskCanceledException();

        QuickstartCatalog result = await fixture.Catalog.SearchAsync(new(), CancellationToken.None);

        result.Provenance.Origin.Should().Be(QuickstartCatalogOrigin.StaleCache);
        result.Entries[0].MetadataStatus.Should().Be(QuickstartPackageMetadataStatus.Unverified);
    }

    [Theory]
    [InlineData(QuickstartCatalogOrigin.Download)]
    [InlineData(QuickstartCatalogOrigin.FreshCache)]
    [InlineData(QuickstartCatalogOrigin.RevalidatedCache)]
    [InlineData(QuickstartCatalogOrigin.StaleCache)]
    public async Task SearchAsync_MetadataTransport_PreservesSourceAndNeverApproves(object transport)
    {
        var origin = (QuickstartCatalogOrigin)transport;
        using Fixture fixture = new(Manifest(Entry("sample", Reference)), origin);

        QuickstartCatalog result = await fixture.Catalog.SearchAsync(new(), CancellationToken.None);

        result.Provenance.Origin.Should().Be(origin);
        result.Provenance.SourceUrl.Should().Be(ManifestUrl);
        result.Entries[0].MetadataStatus.Should().Be(QuickstartPackageMetadataStatus.Unverified);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://old.example.test/manifest.json")]
    public async Task SearchAsync_OfflineCacheFromUnknownOrDifferentSource_DoesNotRelabelIt(string? source)
    {
        using Fixture fixture = new(Manifest(Entry("cached", Reference)), origin: QuickstartCatalogOrigin.StaleCache, cachedSource: source);

        QuickstartCatalog result = await fixture.Catalog.SearchAsync(new(), CancellationToken.None);

        result.Provenance.Origin.Should().Be(QuickstartCatalogOrigin.StaleCache);
        result.Provenance.SourceUrl.Should().Be(source);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://old.example.test/manifest.json")]
    public async Task SearchAsync_UnrelatedCacheOn304_IsNotRevalidated(string? source)
    {
        using Fixture fixture = new(Manifest(Entry("cached", Reference)), origin: QuickstartCatalogOrigin.RevalidatedCache, cachedSource: source);

        QuickstartCatalog result = await fixture.Catalog.SearchAsync(new(), CancellationToken.None);

        fixture.Handler.ConditionalEtags.Should().ContainSingle().Which.Should().BeNull();
        result.Provenance.SourceUrl.Should().Be(source);
        result.Provenance.Origin.Should().Be(QuickstartCatalogOrigin.StaleCache);
        fixture.Cache.DidNotReceive().WriteMeta(Arg.Any<ManifestCacheMeta>());
    }

    [Fact]
    public async Task SearchAsync_SameSourceOn304_RevalidatesWithRetainedEtag()
    {
        using Fixture fixture = new(Manifest(Entry("cached", Reference)), origin: QuickstartCatalogOrigin.RevalidatedCache);

        QuickstartCatalog result = await fixture.Catalog.SearchAsync(new(), CancellationToken.None);

        fixture.Handler.ConditionalEtags.Should().Equal("\"etag\"");
        result.Provenance.Origin.Should().Be(QuickstartCatalogOrigin.RevalidatedCache);
        fixture.Cache.Received(1).WriteMeta(Arg.Is<ManifestCacheMeta>(meta => meta.SourceUrl == ManifestUrl && meta.ETag == "\"etag\""));
    }

    [Fact]
    public async Task SearchAsync_NetworkFailureWithNoCache_IsNotAnEmptySuccess()
    {
        using Fixture fixture = new(Manifest(Entry("sample", Reference)), origin: QuickstartCatalogOrigin.StaleCache);
        fixture.Cache.TryReadManifest().Returns((string?)null);

        await FluentActions.Awaiting(() => fixture.Catalog.SearchAsync(new(), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SearchAsync_MalformedResponse_FallsBackToCacheWithItsOriginalProvenance()
    {
        using Fixture fixture = new("{not json", origin: QuickstartCatalogOrigin.Download);
        fixture.Cache.TryReadManifest().Returns(Manifest(Entry("cached", Reference)));

        QuickstartCatalog result = await fixture.Catalog.SearchAsync(new(), CancellationToken.None);

        result.Entries.Should().ContainSingle().Which.Entry.Id.Should().Be("cached");
        result.Provenance.Origin.Should().Be(QuickstartCatalogOrigin.StaleCache);
        result.Provenance.SourceUrl.Should().BeNull();
        fixture.Cache.DidNotReceive().WriteManifest(Arg.Any<string>());
    }

    [Fact]
    public async Task SearchAsync_MalformedFreshCache_IsAnExplicitFailure()
    {
        using Fixture fixture = new("{not json");

        await FluentActions.Awaiting(() => fixture.Catalog.SearchAsync(new(), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*missing or corrupt*");
    }

    [Fact]
    public void AddQuickstartManifest_SharesLoaderWithoutRequiringAnInstalledStore()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuickstartManifest();
        services.AddSingleton(Substitute.For<IManifestCache>());
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IQuickstartManifestService>()
            .Should().BeSameAs(provider.GetRequiredService<IQuickstartCatalogSource>());
        provider.GetRequiredService<IQuickstartCatalogService>().Should().BeOfType<QuickstartCatalogService>();
    }

    [Fact]
    public async Task SearchAsync_NullQuery_RejectsBeforeLoading()
    {
        IQuickstartCatalogSource source = Substitute.For<IQuickstartCatalogSource>();
        QuickstartCatalogService catalog = new(source);

        await FluentActions.Awaiting(() => catalog.SearchAsync(null!, CancellationToken.None))
            .Should().ThrowAsync<ArgumentNullException>();
        await source.DidNotReceive().GetCatalogDocumentAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Constructor_NullSource_RejectsDependency()
    {
        FluentActions.Invoking(() => new QuickstartCatalogService(null!)).Should().Throw<ArgumentNullException>();
    }

    private static IProjectStack CreateStack()
    {
        IProjectStack stack = Substitute.For<IProjectStack>();
        stack.Stack.Returns("dotnet");
        stack.DisplayName.Returns(".NET");
        stack.WorkerRuntimeAliases.Returns([]);
        stack.SupportedLanguages.Returns(["C#"]);
        stack.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>>());
        return stack;
    }

    private static string Manifest(params string[] entries) => $$"""{"templates":[{{string.Join(",", entries)}}]}""";

    private static string Entry(string id, string? metadata, string language = "Python", string resource = "http",
        string iac = "bicep", int priority = 100)
    {
        string extension = metadata is null ? string.Empty : $",\"packageReference\":{metadata}";
        return $$"""
            {"id":"{{id}}","displayName":"Sample","language":"{{language}}","resource":"{{resource}}","iac":"{{iac}}",
             "repositoryUrl":"https://github.com/Azure-Samples/test-repo","folderPath":".","gitRef":"refs/tags/v1.0.0",
             "priority":{{priority}}{{extension}}}
            """;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _client;

        public Fixture(string json, QuickstartCatalogOrigin origin = QuickstartCatalogOrigin.FreshCache, string? cachedSource = ManifestUrl)
        {
            DateTimeOffset now = new(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);
            Cache = Substitute.For<IManifestCache>();
            Cache.TryReadManifest().Returns(json);
            Cache.ManifestExists().Returns(true);
            if (origin != QuickstartCatalogOrigin.Download)
            {
                Cache.TryReadMeta().Returns(new ManifestCacheMeta("\"etag\"",
                    origin == QuickstartCatalogOrigin.FreshCache ? now : now.AddDays(-2), cachedSource));
            }

            Handler = new(json, origin);
            _client = new(Handler);
            IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
            factory.CreateClient(QuickstartRegistration.HttpClientName).Returns(_client);
            Source = new(factory, Cache, Options.Create(new QuickstartManifestOptions { ManifestUrl = ManifestUrl }),
                new FixedTimeProvider(now), NullLogger<QuickstartManifestService>.Instance);
            Catalog = new(Source);
        }

        public IManifestCache Cache { get; }
        public ManifestHandler Handler { get; }
        public QuickstartManifestService Source { get; }
        public QuickstartCatalogService Catalog { get; }
        public void Dispose() => _client.Dispose();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ManifestHandler(string json, QuickstartCatalogOrigin origin) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public List<string?> ConditionalEtags { get; } = [];
        public Action? BeforeResponse { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            ConditionalEtags.Add(request.Headers.IfNoneMatch.SingleOrDefault()?.Tag);
            request.RequestUri.AbsoluteUri.Should().Be(ManifestUrl);
            BeforeResponse?.Invoke();
            if (origin == QuickstartCatalogOrigin.StaleCache)
            {
                throw new HttpRequestException("Offline");
            }

            return Task.FromResult(origin == QuickstartCatalogOrigin.RevalidatedCache
                ? new HttpResponseMessage(HttpStatusCode.NotModified)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
