// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Tests.Workloads.Catalog;

public sealed class NuGetProtocolSourceClientRawTransportTests
{
    private static readonly PackageSource _source = new("https://raw-transport.test/v3/index.json", "raw-transport")
    {
        ProtocolVersion = 3,
    };

    public static TheoryData<string, string, bool> NonStringPresentationMetadata
    {
        get
        {
            string[] fields = ["title", "description"];
            string[] values = ["{}", "[]", "42", "1.5", "true"];
            TheoryData<string, string, bool> cases = [];
            foreach (string fieldName in fields)
            {
                foreach (string value in values)
                {
                    cases.Add(fieldName, value, false);
                    cases.Add(fieldName, value, true);
                }
            }

            return cases;
        }
    }

    public static TheoryData<string, bool> WrappedSchemaFailures
    {
        get
        {
            string[] failures =
            [
                "json-reader", "json-serialization", "invalid-data", "argument", "argument-null",
                "invalid-operation", "format", "invalid-cast", "overflow",
            ];
            TheoryData<string, bool> cases = [];
            foreach (string failure in failures)
            {
                cases.Add(failure, false);
                cases.Add(failure, true);
            }

            return cases;
        }
    }

    public static TheoryData<string, bool> WhitespaceDeclarations
    {
        get
        {
            string[] prefixes = [WorkloadPackageTags.KindPrefix, WorkloadPackageTags.RuntimeIdentifierPrefix];
            string[] whitespace = ["\t", "\r\n", "\u00a0"];
            TheoryData<string, bool> cases = [];
            foreach (string prefix in prefixes)
            {
                string value = prefix == WorkloadPackageTags.KindPrefix ? "workload" : "win-x64";
                string other = prefix == WorkloadPackageTags.KindPrefix ? "content" : "linux-x64";
                foreach (string padding in whitespace)
                {
                    string[] declarations =
                    [
                        $"{prefix}{value},{padding}{prefix}{value}",
                        $"{prefix}{value},{padding}{prefix.ToUpperInvariant()}{other}",
                        $"{padding}{prefix}{padding}",
                    ];
                    foreach (string declaration in declarations)
                    {
                        cases.Add(declaration, false);
                        cases.Add(declaration, true);
                    }
                }
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(WrappedSchemaFailures))]
    public async Task SearchPageAsync_WrappedIndexSchemaFailure_PreservesExceptionChain(string failure, bool nested)
    {
        Exception cause = SchemaFailure(failure);
        FatalProtocolException fatal = WrapIndexFailure(cause, nested);
        StubServiceIndexProvider provider = new(_ => throw fatal);
        RawTransportClient client = NewClient(_ => Task.FromResult<JObject?>(Page()), provider);
        using CancellationTokenSource cancellation = new();

        var rejected = await FluentActions.Awaiting(() => client.SearchPageAsync(new CatalogSearchQuery(), cancellation.Token))
            .Should().ThrowExactlyAsync<InvalidWorkloadSourceException>();

        rejected.Which.InnerException.Should().BeSameAs(fatal);
        rejected.Which.GetBaseException().Should().BeSameAs(cause);
        provider.CancellationTokens.Should().Equal([cancellation.Token]);
        client.Requests.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(WrappedSchemaFailures))]
    public async Task GetStacksAsync_WrappedIndexSchemaFailure_RejectsWithoutCaching(string failure, bool nested)
    {
        Exception cause = SchemaFailure(failure);
        FatalProtocolException fatal = WrapIndexFailure(cause, nested);
        StubServiceIndexProvider provider = new(_ => throw fatal);
        RawTransportClient client = NewClient(_ => Task.FromResult<JObject?>(Page()), provider);
        SetupStackCatalog discovery = new(NewCatalog(client));
        using CancellationTokenSource cancellation = new();

        for (int attempt = 0; attempt < 2; attempt++)
        {
            var rejected = await FluentActions.Awaiting(() => discovery.GetStacksAsync(null, false, cancellation.Token))
                .Should().ThrowExactlyAsync<SetupConfigurationException>();

            rejected.Which.InnerException.Should().BeOfType<InvalidWorkloadSourceException>()
                .Which.InnerException.Should().BeSameAs(fatal);
            rejected.Which.GetBaseException().Should().BeSameAs(cause);
        }

        provider.CancellationTokens.Should().Equal([cancellation.Token, cancellation.Token]);
        client.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStacksAsync_RawServiceIndexMultipleIdsArray_RejectsNuGetTokenConversionFailure()
    {
        var index = JObject.Parse("""
            {"version":"3.0.0","resources":[{"@id":["https://raw-transport.test/query","https://raw-transport.test/other-query"],"@type":"SearchQueryService"}]}
            """);
        FatalProtocolException? wrapped = null;
        StubServiceIndexProvider provider = new(_ =>
        {
            try
            {
                return new ServiceIndexResourceV3(index, DateTime.UnixEpoch);
            }
            catch (InvalidOperationException ex)
            {
                // Reproduce the fatal wrapper applied by NuGet's service-index provider.
                wrapped = new FatalProtocolException("Unable to load the service index.", ex);
                throw wrapped;
            }
        });
        RawTransportClient client = NewClient(_ => Task.FromResult<JObject?>(Page()), provider);
        SetupStackCatalog discovery = new(NewCatalog(client));
        using CancellationTokenSource cancellation = new();

        var rejected = await FluentActions.Awaiting(() => discovery.GetStacksAsync(null, false, cancellation.Token))
            .Should().ThrowExactlyAsync<SetupConfigurationException>();

        wrapped.Should().NotBeNull();
        wrapped!.InnerException.Should().BeOfType<InvalidOperationException>();
        rejected.Which.InnerException.Should().BeOfType<InvalidWorkloadSourceException>()
            .Which.InnerException.Should().BeSameAs(wrapped);
        provider.CancellationTokens.Should().Equal([cancellation.Token]);
        client.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStacksAsync_RawServiceIndexSingleIdArray_DiscoversStack()
    {
        var index = JObject.Parse("""
            {"version":"3.0.0","resources":[{"@id":["https://raw-transport.test/query"],"@type":"SearchQueryService"}]}
            """);
        StubServiceIndexProvider provider = new(_ => new ServiceIndexResourceV3(index, DateTime.UnixEpoch));
        RawTransportClient client = NewClient(_ => Task.FromResult<JObject?>(Page()), provider);
        SetupStackCatalog discovery = new(NewCatalog(client));
        using CancellationTokenSource cancellation = new();

        SetupStackSnapshot snapshot = await discovery.GetStacksAsync(null, false, cancellation.Token);

        snapshot.StackNames.Should().Equal(["node"]);
        snapshot.StackPackageId("node").Should().Be("contoso.node");
        provider.CancellationTokens.Should().Equal([cancellation.Token]);
        client.Requests.Should().ContainSingle().Which.GetLeftPart(UriPartial.Path).Should().Be("https://raw-transport.test/query");
        client.CancellationTokens.Should().Equal([cancellation.Token]);
    }

    [Theory]
    [InlineData("json-reader")]
    [InlineData("json-serialization")]
    public async Task SearchPageAsync_FetchThrowsJsonException_PreservesInnerException(string failure)
    {
        Exception cause = SchemaFailure(failure);
        RawTransportClient client = NewClient(_ => Task.FromException<JObject?>(cause));
        using CancellationTokenSource cancellation = new();

        var rejected = await FluentActions.Awaiting(() => client.SearchPageAsync(new CatalogSearchQuery(), cancellation.Token))
            .Should().ThrowExactlyAsync<InvalidDataException>();

        rejected.Which.InnerException.Should().BeSameAs(cause);
        client.Requests.Should().ContainSingle();
        client.CancellationTokens.Should().Equal([cancellation.Token]);
    }

    [Theory]
    [InlineData("{\"data\":[")]
    [InlineData("{\"data\":[{\"id\":\"contoso.node\",\"version\":invalid}]}")]
    [InlineData("[]")]
    public async Task GetStacksAsync_MalformedWireJson_RejectsThroughTheRealPageApiWithoutCaching(string wireJson)
    {
        RawTransportClient client = NewClient(_ => Task.FromResult<JObject?>(JObject.Parse(wireJson)));
        SetupStackCatalog discovery = new(NewCatalog(client));
        using CancellationTokenSource cancellation = new();

        for (int attempt = 0; attempt < 2; attempt++)
        {
            var rejected = await FluentActions.Awaiting(() => discovery.GetStacksAsync(null, false, cancellation.Token))
                .Should().ThrowExactlyAsync<SetupConfigurationException>();

            rejected.Which.InnerException.Should().BeOfType<InvalidDataException>()
                .Which.InnerException.Should().BeAssignableTo<JsonException>();
        }

        client.Requests.Should().HaveCount(2);
        client.CancellationTokens.Should().Equal([cancellation.Token, cancellation.Token]);
    }

    [Theory]
    [InlineData("json-reader")]
    [InlineData("json-serialization")]
    public async Task SearchAsync_FetchThrowsJsonException_PreservesLegacyException(string failure)
    {
        Exception cause = SchemaFailure(failure);
        RawTransportClient client = NewClient(_ => Task.FromException<JObject?>(cause));
        using CancellationTokenSource cancellation = new();

        Exception? rejected = await Record.ExceptionAsync(() => client.SearchAsync(new CatalogSearchQuery(), cancellation.Token));

        rejected.Should().BeSameAs(cause);
        client.CancellationTokens.Should().Equal([cancellation.Token]);
    }

    [Theory]
    [InlineData("http", false)]
    [InlineData("io", false)]
    [InlineData("fatal", false)]
    [InlineData("fatal-http", false)]
    [InlineData("fatal-io", false)]
    [InlineData("http", true)]
    [InlineData("io", true)]
    [InlineData("fatal", true)]
    [InlineData("fatal-http", true)]
    [InlineData("fatal-io", true)]
    public async Task SearchPageAsync_TransportFailure_PropagatesOriginalException(string failure, bool duringFetch)
    {
        Exception cause = TransportFailure(failure);
        StubServiceIndexProvider provider = new(_ => duringFetch ? ServiceIndex(_source) : throw cause);
        RawTransportClient client = NewClient(_ => Task.FromException<JObject?>(cause), provider);
        using CancellationTokenSource cancellation = new();

        Exception? rejected = await Record.ExceptionAsync(() => client.SearchPageAsync(new CatalogSearchQuery(), cancellation.Token));

        rejected.Should().BeSameAs(cause);
        provider.CancellationTokens.Should().Equal([cancellation.Token]);
        client.Requests.Should().HaveCount(duringFetch ? 1 : 0);
    }

    [Theory]
    [InlineData("http", false)]
    [InlineData("io", false)]
    [InlineData("fatal", false)]
    [InlineData("fatal-http", false)]
    [InlineData("fatal-io", false)]
    [InlineData("http", true)]
    [InlineData("io", true)]
    [InlineData("fatal", true)]
    [InlineData("fatal-http", true)]
    [InlineData("fatal-io", true)]
    public async Task GetStacksAsync_TransportFailure_CachesBuiltInFallback(string failure, bool duringFetch)
    {
        Exception cause = TransportFailure(failure);
        StubServiceIndexProvider provider = new(_ => duringFetch ? ServiceIndex(_source) : throw cause);
        RawTransportClient client = NewClient(_ => Task.FromException<JObject?>(cause), provider);
        SetupStackCatalog discovery = new(NewCatalog(client));
        using CancellationTokenSource cancellation = new();

        SetupStackSnapshot snapshot = await discovery.GetStacksAsync(null, false, cancellation.Token);
        SetupStackSnapshot cached = await discovery.GetStacksAsync(null, false, cancellation.Token);

        snapshot.Should().BeSameAs(SetupDependency.BuiltInStackSnapshot);
        cached.Should().BeSameAs(snapshot);
        provider.CancellationTokens.Should().Equal([cancellation.Token]);
        client.Requests.Should().HaveCount(duringFetch ? 1 : 0);
    }

    [Theory]
    [InlineData("argument", false)]
    [InlineData("invalid-operation", false)]
    [InlineData("format", false)]
    [InlineData("argument", true)]
    [InlineData("invalid-operation", true)]
    [InlineData("format", true)]
    public async Task GetStacksAsync_UnwrappedNonJsonFailure_PropagatesWithoutCaching(string failure, bool duringFetch)
    {
        Exception cause = SchemaFailure(failure);
        StubServiceIndexProvider provider = new(_ => duringFetch ? ServiceIndex(_source) : throw cause);
        RawTransportClient client = NewClient(_ => Task.FromException<JObject?>(cause), provider);
        SetupStackCatalog discovery = new(NewCatalog(client));
        using CancellationTokenSource cancellation = new();

        Exception? first = await Record.ExceptionAsync(() => discovery.GetStacksAsync(null, false, cancellation.Token));
        Exception? second = await Record.ExceptionAsync(() => discovery.GetStacksAsync(null, false, cancellation.Token));

        first.Should().BeSameAs(cause);
        second.Should().BeSameAs(cause);
        provider.CancellationTokens.Should().Equal([cancellation.Token, cancellation.Token]);
        client.Requests.Should().HaveCount(duringFetch ? 2 : 0);
    }

    [Fact]
    public async Task SearchPageAsync_FatalAtSearchFetch_DoesNotApplyServiceIndexClassification()
    {
        FatalProtocolException fatal = new("Search request failed.", new InvalidOperationException("Search resource failure."));
        RawTransportClient client = NewClient(_ => Task.FromException<JObject?>(fatal));
        using CancellationTokenSource cancellation = new();

        Exception? rejected = await Record.ExceptionAsync(() => client.SearchPageAsync(new CatalogSearchQuery(), cancellation.Token));

        rejected.Should().BeSameAs(fatal);
        client.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetStacksAsync_CancellationAtResourceOrFetch_PropagatesWithoutFallback(bool duringFetch)
    {
        using CancellationTokenSource cancellation = new();
        OperationCanceledException cause = new(cancellation.Token);
        StubServiceIndexProvider provider = new(_ => duringFetch ? ServiceIndex(_source) : throw cause);
        RawTransportClient client = NewClient(_ => Task.FromException<JObject?>(cause), provider);
        SetupStackCatalog discovery = new(NewCatalog(client));

        Exception? rejected = await Record.ExceptionAsync(() => discovery.GetStacksAsync(null, false, cancellation.Token));

        rejected.Should().BeSameAs(cause);
        provider.CancellationTokens.Should().Equal([cancellation.Token]);
        client.Requests.Should().HaveCount(duringFetch ? 1 : 0);
    }

    [Theory]
    [MemberData(nameof(WhitespaceDeclarations))]
    public async Task SearchPageAsync_WhitespaceHiddenDuplicateOrEmptyKindAndRid_Rejects(string declaration, bool arrayTags)
    {
        JObject row = StackRow();
        row["tags"] = Tags(declaration, arrayTags);
        RawTransportClient client = NewClient(_ => Task.FromResult<JObject?>(Page(row)));
        using CancellationTokenSource cancellation = new();

        await FluentActions.Awaiting(() => client.SearchPageAsync(new CatalogSearchQuery(), cancellation.Token))
            .Should().ThrowExactlyAsync<InvalidDataException>();

        client.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("kind:workload,\tkind:content", false)]
    [InlineData("kind:workload,\tkind:content", true)]
    [InlineData("rid:win-x64,\trid:linux-x64", false)]
    [InlineData("rid:win-x64,\trid:linux-x64", true)]
    public async Task GetStacksAsync_WhitespaceHiddenDuplicateKindOrRid_RejectsInsteadOfFallingBack(string declaration, bool arrayTags)
    {
        JObject row = StackRow();
        row["tags"] = Tags(declaration, arrayTags);
        RawTransportClient client = NewClient(_ => Task.FromResult<JObject?>(Page(row)));
        SetupStackCatalog discovery = new(NewCatalog(client));
        using CancellationTokenSource cancellation = new();

        var rejected = await FluentActions.Awaiting(() => discovery.GetStacksAsync(null, false, cancellation.Token))
            .Should().ThrowExactlyAsync<SetupConfigurationException>();

        rejected.Which.InnerException.Should().BeOfType<InvalidDataException>();
    }

    [Theory]
    [InlineData("kind:workload,\tkind:content", "content", null)]
    [InlineData("rid:win-x64,\trid:linux-x64", null, "linux-x64")]
    public async Task SearchAsync_DuplicateDeclarations_KeepsLegacyLastValueParsing(string declaration, string? kind, string? rid)
    {
        JObject row = StackRow();
        row["tags"] = declaration;
        RawTransportClient client = NewClient(_ => Task.FromResult<JObject?>(Page(row)));
        using CancellationTokenSource cancellation = new();

        var results = await client.SearchAsync(new CatalogSearchQuery(), cancellation.Token);

        CatalogSearchResult hit = results.Should().ContainSingle().Subject;
        hit.Kind.Should().Be(kind);
        hit.Rid.Should().Be(rid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchPageAsync_PaddedValidTags_PreservesMultipleAliasesAndCanonicalStack(bool arrayTags)
    {
        JObject row = StackRow();
        row["tags"] = Tags("\tkind:workload,\r\nrid:win-x64,\talias:javascript,\talias:node,\r\nstack:node", arrayTags);
        RawTransportClient client = NewClient(_ => Task.FromResult<JObject?>(Page(row)));
        using CancellationTokenSource cancellation = new();

        CatalogSearchPage page = await client.SearchPageAsync(new CatalogSearchQuery(), cancellation.Token);

        CatalogSearchResult hit = page.Items.Should().ContainSingle().Subject;
        hit.Kind.Should().Be("workload");
        hit.Rid.Should().Be("win-x64");
        hit.Aliases.Should().Equal(["javascript", "node"]);
        hit.CanonicalStack.Should().Be("node");
        page.RawCount.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetStacksAsync_RepeatedIdenticalStackDeclaration_RemainsInvalid(bool arrayTags)
    {
        JObject row = StackRow();
        row["tags"] = Tags("kind:workload,alias:node,alias:javascript,stack:node,\tstack:node", arrayTags);
        RawTransportClient client = NewClient(_ => Task.FromResult<JObject?>(Page(row)));
        SetupStackCatalog discovery = new(NewCatalog(client));
        using CancellationTokenSource cancellation = new();

        await FluentActions.Awaiting(() => discovery.GetStacksAsync(null, false, cancellation.Token))
            .Should().ThrowExactlyAsync<SetupConfigurationException>();
    }

    [Theory]
    [InlineData("\"FuncCliWorkload\"")]
    [InlineData("[null]")]
    [InlineData("[42]")]
    [InlineData("[{}]")]
    [InlineData("[{\"name\":null}]")]
    [InlineData("[{\"name\":42}]")]
    [InlineData("[\"\"]")]
    [InlineData("[\" \\t\\r\\n\"]")]
    [InlineData("[{\"name\":\"\"}]")]
    [InlineData("[{\"name\":\" \\t\\r\\n\"}]")]
    [InlineData("[\"FuncCliWorkload\",\" \\t\"]")]
    [InlineData("[{\"name\":\"FuncCliWorkload\"},{}]")]
    [InlineData("[\"Dependency\",{}]")]
    [InlineData("[{\"name\":\"Dependency\"},{\"name\":42}]")]
    public async Task SearchPageAsync_MalformedOrBlankPackageTypeName_RejectsEntirePage(string packageTypes)
    {
        JObject row = StackRow();
        row["packageTypes"] = JToken.Parse(packageTypes);
        RawTransportClient client = NewClient(_ => Task.FromResult<JObject?>(Page(row)));
        using CancellationTokenSource cancellation = new();

        await FluentActions.Awaiting(() => client.SearchPageAsync(new CatalogSearchQuery(), cancellation.Token))
            .Should().ThrowExactlyAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(null, true, false)]
    [InlineData(null, true, true)]
    [InlineData("null", true, false)]
    [InlineData("null", true, true)]
    [InlineData("[]", false, false)]
    [InlineData("[]", false, true)]
    [InlineData("[\"funccliworkload\"]", true, false)]
    [InlineData("[\"funccliworkload\"]", true, true)]
    [InlineData("[{\"name\":\"FuncCliWorkload\"}]", true, false)]
    [InlineData("[{\"name\":\"FuncCliWorkload\"}]", true, true)]
    [InlineData("[\"Dependency\"]", false, false)]
    [InlineData("[\"Dependency\"]", false, true)]
    [InlineData("[{\"name\":\"Dependency\"}]", false, false)]
    [InlineData("[{\"name\":\"Dependency\"}]", false, true)]
    public async Task SearchApis_OptionalOrValidPackageTypes_PreservesCompatibility(string? packageTypes, bool retained, bool legacySearch)
    {
        JObject row = StackRow();
        if (packageTypes is not null)
        {
            row["packageTypes"] = JToken.Parse(packageTypes);
        }

        RawTransportClient client = NewClient(_ => Task.FromResult<JObject?>(Page(row)));
        using CancellationTokenSource cancellation = new();

        if (legacySearch)
        {
            var items = await client.SearchAsync(new CatalogSearchQuery(), cancellation.Token);
            items.Should().HaveCount(retained ? 1 : 0);
        }
        else
        {
            CatalogSearchPage page = await client.SearchPageAsync(new CatalogSearchQuery(), cancellation.Token);
            page.Items.Should().HaveCount(retained ? 1 : 0);
            page.RawCount.Should().Be(1);
            page.TotalHits.Should().Be(1);
        }
    }

    [Fact]
    public async Task Discovery_ExplicitEmptyTypes_CannotClaimAStackOrHideLaterPages()
    {
        List<int> skips = [];
        RawTransportClient client = new(new SourceRepository(_source,
            [new Lazy<INuGetResourceProvider>(() => new StubServiceIndexProvider(_ => ServiceIndex(_source)))]),
            _ => throw new InvalidOperationException("Use offset-aware response"))
        {
            ResponseForUri = uri =>
            {
                int skip = int.Parse(uri.Query.Split('&').Single(part => part.StartsWith("skip=", StringComparison.Ordinal))[5..]);
                skips.Add(skip);
                if (skip == 0)
                {
                    JArray rows = [];
                    for (int i = 0; i < 100; i++)
                    {
                        JObject row = StackRow();
                        row["id"] = $"untyped.node.{i}";
                        row["packageTypes"] = new JArray();
                        rows.Add(row);
                    }

                    return new JObject { ["data"] = rows, ["totalHits"] = 101 };
                }

                skip.Should().Be(100);
                return new JObject { ["data"] = new JArray(StackRow()), ["totalHits"] = 101 };
            },
        };

        SetupStackSnapshot snapshot = await new SetupStackCatalog(NewCatalog(client)).GetStacksAsync(null, false, CancellationToken.None);

        skips.Should().Equal(0, 100);
        snapshot.IsFallback.Should().BeFalse();
        snapshot.IsAmbiguous("node").Should().BeFalse();
        snapshot.StackPackageId("node").Should().Be("contoso.node");
    }

    [Theory]
    [MemberData(nameof(NonStringPresentationMetadata))]
    public async Task SearchApis_NonStringPresentationMetadata_IsIgnoredWithoutLosingOwnership(string field, string value, bool legacySearch)
    {
        JObject row = StackRow();
        row["tags"] = "kind:workload rid:any alias:javascript alias:node stack:node";
        row["title"] = "Node stack";
        row["description"] = "Node workload";
        row[field] = JToken.Parse(value);
        RawTransportClient client = NewClient(_ => Task.FromResult<JObject?>(Page(row)));
        using CancellationTokenSource cancellation = new();

        IReadOnlyList<CatalogSearchResult> results;
        if (legacySearch)
        {
            results = await client.SearchAsync(new CatalogSearchQuery(), cancellation.Token);
        }
        else
        {
            CatalogSearchPage page = await client.SearchPageAsync(new CatalogSearchQuery(), cancellation.Token);
            page.RawCount.Should().Be(1);
            page.TotalHits.Should().Be(1);
            results = page.Items;
        }

        CatalogSearchResult hit = results.Should().ContainSingle().Subject;
        hit.PackageId.Should().Be("contoso.node");
        hit.LatestVersion.ToNormalizedString().Should().Be("1.0.0");
        hit.Title.Should().Be(field == "title" ? null : "Node stack");
        hit.Description.Should().Be(field == "description" ? null : "Node workload");
        hit.Aliases.Should().Equal(["javascript", "node"]);
        hit.CanonicalStack.Should().Be("node");
        hit.Kind.Should().Be("workload");
        hit.Rid.Should().Be("any");
        hit.Source.Should().BeSameAs(_source);
        client.CancellationTokens.Should().Equal([cancellation.Token]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"id\":\"\",\"version\":\"1.0.0\"}")]
    [InlineData("{\"id\":\" \\t\",\"version\":\"1.0.0\"}")]
    [InlineData("{\"id\":\"contoso.node\"}")]
    [InlineData("{\"id\":\"contoso.node\",\"version\":\" \\t\"}")]
    public async Task SearchPageAsync_MissingOrWhitespaceIdentity_Rejects(string wireRow)
    {
        RawTransportClient client = NewClient(_ => Task.FromResult<JObject?>(Page(JObject.Parse(wireRow))));
        using CancellationTokenSource cancellation = new();

        await FluentActions.Awaiting(() => client.SearchPageAsync(new CatalogSearchQuery(), cancellation.Token))
            .Should().ThrowExactlyAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(null, null, PackageSourceProvider.DefaultSourceUrl)]
    [InlineData(" \t", "\r\n", PackageSourceProvider.DefaultSourceUrl)]
    [InlineData(" https://configured.test/feed \t", null, "https://configured.test/feed")]
    [InlineData(" https://configured.test/feed \t", " \t", "https://configured.test/feed")]
    [InlineData("https://configured.test/feed", " \thttp://override.test/nuget/v3 \r\n", "http://override.test/nuget/v3")]
    public async Task SearchPageAsync_RealSourceProvider_SelectsV3SourceBeforeResourceAcquisition(
        string? configured, string? sourceOverride, string expected)
    {
        var options = Options.Create(new WorkloadCatalogOptions { Source = configured });
        PackageSourceProvider sourceProvider = new(options);
        List<PackageSource> selectedSources = [];
        List<RawTransportClient> clients = [];
        WorkloadCatalog catalog = new(options, sourceProvider, source =>
        {
            selectedSources.Add(source);
            RawTransportClient client = NewClient(_ => Task.FromResult<JObject?>(Page()), source: source);
            clients.Add(client);
            return client;
        });
        using CancellationTokenSource cancellation = new();

        CatalogSearchPage page = await catalog.SearchPageAsync(new CatalogSearchQuery { Source = sourceOverride }, cancellation.Token);

        PackageSource selected = selectedSources.Should().ContainSingle().Subject;
        selected.Source.Should().Be(expected);
        selected.ProtocolVersion.Should().Be(3);
        selected.IsLocal.Should().BeFalse();
        page.Items.Should().ContainSingle().Which.Source.Should().BeSameAs(selected);
        RawTransportClient selectedClient = clients.Should().ContainSingle().Subject;
        selectedClient.Requests.Should().ContainSingle().Which.GetLeftPart(UriPartial.Path)
            .Should().Be(new Uri(new Uri(expected), "/query").AbsoluteUri);
        selectedClient.CancellationTokens.Should().Equal([cancellation.Token]);
    }

    private static Exception SchemaFailure(string failure) => failure switch
    {
        "json-reader" => new JsonReaderException("Malformed JSON."),
        "json-serialization" => new JsonSerializationException("Malformed JSON value."),
        "invalid-data" => new InvalidDataException("Invalid index data."),
        "argument" => new ArgumentException("Invalid index token."),
        "argument-null" => new ArgumentNullException("value"),
        "invalid-operation" => new InvalidOperationException("Cannot convert array to a scalar."),
        "format" => new FormatException("Invalid index format."),
        "invalid-cast" => new InvalidCastException("Invalid index token type."),
        "overflow" => new OverflowException("Index value is out of range."),
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    };

    private static Exception TransportFailure(string failure) => failure switch
    {
        "http" => new HttpRequestException("Offline."),
        "io" => new IOException("Connection interrupted."),
        "fatal" => new FatalProtocolException("Request failed."),
        "fatal-http" => new FatalProtocolException("Request failed.", new HttpRequestException("Offline.")),
        "fatal-io" => new FatalProtocolException("Request failed.", new IOException("Connection interrupted.")),
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    };

    private static FatalProtocolException WrapIndexFailure(Exception cause, bool nested)
        => new("Unable to load the service index.", nested ? new FatalProtocolException("Invalid index.", cause) : cause);

    private static JObject StackRow() => new()
    {
        ["id"] = "contoso.node",
        ["version"] = "1.0.0",
        ["tags"] = "kind:workload alias:node stack:node",
    };

    private static JToken Tags(string tags, bool arrayTags)
        => arrayTags ? new JArray(tags.Split(',')) : new JValue(tags);

    private static JObject Page(JObject? row = null) => new()
    {
        ["data"] = new JArray(row ?? StackRow()),
        ["totalHits"] = 1,
    };

    private static ServiceIndexResourceV3 ServiceIndex(PackageSource source)
        => new(new JObject
        {
            ["version"] = "3.0.0",
            ["resources"] = new JArray(new JObject
            {
                ["@id"] = new Uri(new Uri(source.Source), "/query").AbsoluteUri,
                ["@type"] = "SearchQueryService/3.5.0",
            }),
        }, DateTime.UnixEpoch);

    private static RawTransportClient NewClient(
        Func<CancellationToken, Task<JObject?>> fetch, StubServiceIndexProvider? provider = null, PackageSource? source = null)
    {
        PackageSource selectedSource = source ?? _source;
        StubServiceIndexProvider selectedProvider = provider ?? new(_ => ServiceIndex(selectedSource));
        SourceRepository repository = new(selectedSource, [new Lazy<INuGetResourceProvider>(() => selectedProvider)]);
        return new RawTransportClient(repository, fetch);
    }

    private static WorkloadCatalog NewCatalog(RawTransportClient client)
    {
        var options = Options.Create(new WorkloadCatalogOptions { Source = client.Source.Source });
        return new WorkloadCatalog(options, new PackageSourceProvider(options), _ => client);
    }

    private sealed class StubServiceIndexProvider(Func<CancellationToken, ServiceIndexResourceV3> create) : INuGetResourceProvider
    {
        private readonly Func<CancellationToken, ServiceIndexResourceV3> _create = create ?? throw new ArgumentNullException(nameof(create));

        public Type ResourceType => typeof(ServiceIndexResourceV3);

        public string Name => nameof(StubServiceIndexProvider);

        public IEnumerable<string> Before => [];

        public IEnumerable<string> After => [];

        public List<CancellationToken> CancellationTokens { get; } = [];

        public Task<Tuple<bool, INuGetResource?>> TryCreate(SourceRepository source, CancellationToken token)
        {
            CancellationTokens.Add(token);
            return Task.FromResult(Tuple.Create<bool, INuGetResource?>(true, _create(token)));
        }
    }

    private sealed class RawTransportClient(SourceRepository repository, Func<CancellationToken, Task<JObject?>> fetch)
        : NuGetProtocolSourceClient(repository)
    {
        private readonly Func<CancellationToken, Task<JObject?>> _fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));

        public List<Uri> Requests { get; } = [];

        public List<CancellationToken> CancellationTokens { get; } = [];

        public Func<Uri, JObject>? ResponseForUri { get; init; }

        internal override Task<JObject?> FetchSearchResponseAsync(Uri searchUri, CancellationToken cancellationToken)
        {
            Requests.Add(searchUri);
            CancellationTokens.Add(cancellationToken);
            if (ResponseForUri is not null) return Task.FromResult<JObject?>(ResponseForUri(searchUri));
            return _fetch(cancellationToken);
        }
    }
}
