// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Tests.Workloads.Catalog;

public sealed class DiscoveryRawFeedAuditTests
{
    private static readonly PackageSource _source = new("https://raw-feed.test/v3/index.json", "raw-feed");
    private static readonly PackageSource _overrideSource = new("https://override-feed.test/v3/index.json", "override-feed");

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("""{"id":42,"version":"1.0.0"}""")]
    [InlineData("""{"id":{"value":"contoso.node"},"version":"1.0.0"}""")]
    [InlineData("""{"id":"contoso.node"}""")]
    [InlineData("""{"id":"contoso.node","version":"not-a-version"}""")]
    [InlineData("""{"id":"contoso.node","version":{"value":"1.0.0"}}""")]
    [InlineData("""{"id":"contoso.node","version":"1.0.0","tags":["alias:node",{"value":"stack:node"}]}""")]
    [InlineData("""{"id":"contoso.node","version":"1.0.0","packageTypes":{"name":"FuncCliWorkload"}}""")]
    public async Task SearchPageAsync_MalformedInnerRow_RejectsTheEntirePage(string malformedRow)
    {
        JObject response = Page(new JArray(
            StackRow("first.node"),
            JToken.Parse(malformedRow),
            StackRow("second.node")), totalHits: 3);
        RawFeedClient client = NewClient((_, _) => response);
        using CancellationTokenSource cancellation = new();

        await FluentActions.Awaiting(() => client.SearchPageAsync(new CatalogSearchQuery(), cancellation.Token))
            .Should().ThrowExactlyAsync<InvalidDataException>();

        client.Requests.Should().ContainSingle();
        client.RawCounts.Should().Equal([3]);
        client.CancellationTokens.Should().Equal([cancellation.Token]);
    }

    [Theory]
    [InlineData(0, "null")]
    [InlineData(1, """{"id":"malformed.node"}""")]
    [InlineData(2, """{"id":"malformed.node","version":{"value":"1.0.0"}}""")]
    public async Task GetStacksAsync_BadRowBeforeBetweenOrAfterNodeClaimants_RejectsInsteadOfFallingBack(int badRowIndex, string malformedRow)
    {
        JArray rows = new(StackRow("first.node"), StackRow("second.node"));
        rows.Insert(badRowIndex, JToken.Parse(malformedRow));
        RawFeedClient client = NewClient((_, _) => Page(rows, totalHits: 3));
        SetupStackCatalog discovery = new(NewCatalog(client));
        using CancellationTokenSource cancellation = new();

        var failure = await FluentActions.Awaiting(() => discovery.GetStacksAsync(null, false, cancellation.Token))
            .Should().ThrowExactlyAsync<SetupConfigurationException>();

        failure.Which.InnerException.Should().BeOfType<InvalidDataException>();
        client.Requests.Should().ContainSingle();
        client.RawCounts.Should().Equal([3]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"id":{"value":"unrelated"},"version":"1.0.0"}""")]
    [InlineData("""{"id":"unrelated","version":{"value":"1.0.0"}}""")]
    [InlineData("""{"id":"unrelated","version":"not-a-version"}""")]
    [InlineData("""{"id":"unrelated","version":"1.0.0","tags":{"value":"alias:node"}}""")]
    [InlineData("""{"id":"unrelated","version":"1.0.0","tags":["alias:node",{}]}""")]
    [InlineData("""{"id":"unrelated","version":"1.0.0","tags":"kind:workload kind:content rid:win-x64 rid:linux-x64"}""")]
    [InlineData("""{"id":"unrelated","version":"1.0.0","title":{"value":"Node"}}""")]
    [InlineData("""{"id":"unrelated","version":"1.0.0","description":["Node"]}""")]
    public async Task SearchPageAsync_KnownNonWorkloadMalformedFields_AreIgnoredAndCounted(string malformedRow)
    {
        var stringTypeRow = JObject.Parse(malformedRow);
        stringTypeRow["packageTypes"] = new JArray("Dependency");
        var objectTypeRow = JObject.Parse(malformedRow);
        objectTypeRow["packageTypes"] = new JArray(new JObject { ["name"] = "Dependency" });
        JObject workload = StackRow("contoso.node", "kind:workload alias:javascript alias:node stack:node");
        JObject response = Page(new JArray(stringTypeRow, workload, objectTypeRow), totalHits: 3);
        RawFeedClient client = NewClient((_, _) => response);
        using CancellationTokenSource cancellation = new();

        CatalogSearchPage page = await client.SearchPageAsync(new CatalogSearchQuery(), cancellation.Token);

        CatalogSearchResult hit = page.Items.Should().ContainSingle().Subject;
        hit.PackageId.Should().Be("contoso.node");
        hit.Aliases.Should().Equal(["javascript", "node"]);
        hit.CanonicalStack.Should().Be("node");
        page.RawCount.Should().Be(3);
        page.TotalHits.Should().Be(3);
        ((JArray)response["data"]!).Should().HaveCount(3, "filtering must not mutate the raw response");
        client.RawCounts.Should().Equal([3]);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("description")]
    public async Task GetStacksAsync_MalformedPresentationMetadata_RetainsActualPackageAndAliases(string field)
    {
        JObject row = StackRow("contoso.node", "kind:workload alias:javascript alias:node stack:node");
        row[field] = new JObject { ["value"] = "Not an alias" };
        RawFeedClient client = NewClient((_, _) => Page(new JArray(row), totalHits: 1));
        SetupStackCatalog discovery = new(NewCatalog(client));
        using CancellationTokenSource cancellation = new();

        SetupStackSnapshot snapshot = await discovery.GetStacksAsync(null, false, cancellation.Token);

        snapshot.Should().NotBeSameAs(SetupDependency.BuiltInStackSnapshot);
        snapshot.StackNames.Should().Equal(["node"]);
        snapshot.StackPackageId("node").Should().Be("contoso.node");
        snapshot.StackPackageId("javascript").Should().Be("contoso.node");
        snapshot.CanonicalStackName("javascript").Should().Be("node");
        client.Requests.Should().ContainSingle();
        client.RawCounts.Should().Equal([1]);
    }

    [Fact]
    public async Task GetStacksAsync_MalformedNonWorkloadRowsAndFullyFilteredGap_DoNotHideLaterAliasConflicts()
    {
        RawFeedClient client = NewClient((skip, _) =>
        {
            if (skip == 200)
            {
                return Page(new JArray(StackRow("second.node")), totalHits: 201);
            }

            if (skip is not (0 or 100))
            {
                throw new Xunit.Sdk.XunitException($"Unexpected feed offset {skip}.");
            }

            JArray rows = new(Enumerable.Range(0, 100).Select(_ => new JObject
            {
                ["id"] = new JObject(),
                ["version"] = new JArray(),
                ["tags"] = new JObject(),
                ["title"] = new JArray(),
                ["packageTypes"] = new JArray("Dependency"),
            }));
            if (skip == 0)
            {
                rows[0] = StackRow("first.node");
            }

            return Page(rows, totalHits: 201);
        });
        SetupStackCatalog discovery = new(NewCatalog(client));
        using CancellationTokenSource cancellation = new();

        SetupStackSnapshot snapshot = await discovery.GetStacksAsync(null, false, cancellation.Token);

        client.Requests.Select(uri => QueryInt(uri, "skip")).Should().Equal([0, 100, 200]);
        client.RawCounts.Should().Equal([100, 100, 1]);
        snapshot.IsAmbiguous("node").Should().BeTrue();
        snapshot.StackPackageId("node").Should().BeNull();
        snapshot.ConflictsFor("node").Should().ContainSingle()
            .Which.PackageIds.Should().Equal(["first.node", "second.node"]);
    }

    [Theory]
    [InlineData("id", "42")]
    [InlineData("id", "{}")]
    [InlineData("version", "{}")]
    [InlineData("version", "\"not-a-version\"")]
    [InlineData("tags", "[\"alias:node\",{}]")]
    [InlineData("packageTypes", "[\"Dependency\",{}]")]
    public async Task GetStacksAsync_MalformedOwnershipAfterFullyFilteredPage_RejectsWithoutCaching(string field, string value)
    {
        JObject malformed = StackRow("second.node");
        malformed[field] = JToken.Parse(value);
        RawFeedClient client = NewClient((skip, _) =>
        {
            if (skip == 200)
            {
                return Page(new JArray(malformed), totalHits: 201);
            }

            if (skip is not (0 or 100))
            {
                throw new Xunit.Sdk.XunitException($"Unexpected feed offset {skip}.");
            }

            JArray rows = new(Enumerable.Range(0, 100).Select(_ => FilteredRow()));
            if (skip == 0)
            {
                rows[0] = StackRow("first.node");
            }

            return Page(rows, totalHits: 201);
        });
        SetupStackCatalog discovery = new(NewCatalog(client));
        using CancellationTokenSource cancellation = new();

        for (int attempt = 0; attempt < 2; attempt++)
        {
            var failure = await FluentActions.Awaiting(() => discovery.GetStacksAsync(null, false, cancellation.Token))
                .Should().ThrowExactlyAsync<SetupConfigurationException>();

            failure.Which.InnerException.Should().BeOfType<InvalidDataException>();
        }

        client.Requests.Select(uri => QueryInt(uri, "skip")).Should().Equal([0, 100, 200, 0, 100, 200]);
        client.RawCounts.Should().Equal([100, 100, 1, 100, 100, 1]);
    }

    [Theory]
    [InlineData("stack:", false)]
    [InlineData("stack:", true)]
    [InlineData("stack: stack:node", false)]
    [InlineData("stack: stack:node", true)]
    [InlineData("stack:node stack:", false)]
    [InlineData("stack:node stack:", true)]
    public async Task GetStacksAsync_EmptyCanonicalDeclaration_RemainsInvalidThroughThePageApi(string declaration, bool arrayTags)
    {
        string tags = $"kind:workload alias:node {declaration}";
        JObject row = StackRow("contoso.node", tags);
        if (arrayTags)
        {
            row["tags"] = new JArray(tags.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        RawFeedClient client = NewClient((_, _) => Page(new JArray(row), totalHits: 1));
        using CancellationTokenSource cancellation = new();

        CatalogSearchPage page = await client.SearchPageAsync(new CatalogSearchQuery(), cancellation.Token);

        CatalogSearchResult hit = page.Items.Should().ContainSingle().Subject;
        hit.CanonicalStack.Should().NotBeNull("an explicit empty stack tag must not become an absent declaration");
        hit.Aliases.Should().NotContain(hit.CanonicalStack!, "an empty declaration cannot be repaired by another stack tag");
        page.RawCount.Should().Be(1);

        SetupStackCatalog discovery = new(NewCatalog(client));
        var failure = await FluentActions.Awaiting(() => discovery.GetStacksAsync(null, false, cancellation.Token))
            .Should().ThrowExactlyAsync<SetupConfigurationException>();
        failure.Which.Message.Should().Contain("contoso.node")
            .And.Contain("non-empty")
            .And.Contain("exactly once")
            .And.Contain("match a declared alias")
            .And.Contain("may be omitted only when the package has one distinct alias")
            .And.Contain("func workload install --exact <package-id>");
    }

    [Theory]
    [InlineData("stack:", false, false)]
    [InlineData("stack:", true, false)]
    [InlineData("stack:node", false, false)]
    [InlineData("stack:node", true, false)]
    [InlineData("stack:node stack:node", false, false)]
    [InlineData("stack:node stack:node", true, false)]
    [InlineData("stack:", false, true)]
    [InlineData("stack:node stack:node", true, true)]
    public async Task GetStacksAsync_AliaslessExplicitCanonicalDeclaration_RejectsWithoutCaching(
        string declaration, bool arrayTags, bool laterPageFails)
    {
        string tags = $"kind:workload {declaration}";
        JObject row = StackRow("contoso.node", tags);
        if (arrayTags)
        {
            row["tags"] = new JArray(tags.Split(' '));
        }

        bool corrected = false;
        RawFeedClient client = NewClient((skip, _) => skip switch
        {
            0 => Page(new JArray(row, StackRow("contoso.java", "kind:workload alias:java stack:java")),
                totalHits: laterPageFails && !corrected ? 3 : 2),
            2 when laterPageFails && !corrected => throw new HttpRequestException("Later search page unavailable."),
            _ => throw new Xunit.Sdk.XunitException($"Unexpected feed offset {skip}."),
        });
        SetupStackCatalog discovery = new(NewCatalog(client));
        using CancellationTokenSource cancellation = new();

        for (int attempt = 0; attempt < 2; attempt++)
        {
            await FluentActions.Awaiting(() => discovery.GetStacksAsync(null, false, cancellation.Token))
                .Should().ThrowExactlyAsync<SetupConfigurationException>()
                .WithMessage("*contoso.node*invalid canonical stack metadata*");
        }

        const string correctedTags = "kind:workload alias:node stack:node";
        row["tags"] = arrayTags ? new JArray(correctedTags.Split(' ')) : new JValue(correctedTags);
        corrected = true;

        SetupStackSnapshot retried = await discovery.GetStacksAsync(null, false, cancellation.Token);
        SetupStackSnapshot cached = await discovery.GetStacksAsync(null, false, cancellation.Token);

        retried.IsFallback.Should().BeFalse();
        retried.StackNames.Should().BeEquivalentTo(["node", "java"]);
        retried.StackPackageId("node").Should().Be("contoso.node");
        retried.StackPackageId("java").Should().Be("contoso.java");
        cached.Should().BeSameAs(retried);
        client.Requests.Select(uri => QueryInt(uri, "skip"))
            .Should().Equal(laterPageFails ? [0, 2, 0, 2, 0] : [0, 0, 0]);
        client.RawCounts.Should().Equal([2, 2, 2]);
    }

    [Theory]
    [InlineData("kind:workload", false, false)]
    [InlineData("kind:workload", true, false)]
    [InlineData("kind:workload stack:", false, true)]
    [InlineData("kind:workload stack:node stack:node", true, true)]
    [InlineData("kind:content stack:", false, false)]
    [InlineData("kind:meta stack:node", true, false)]
    [InlineData("kind:rid-pointer stack:node stack:node", false, false)]
    [InlineData("kind:unknown stack:node", true, false)]
    [InlineData("stack:node", false, false)]
    public async Task GetStacksAsync_IgnoredCanonicalMetadata_DoesNotRejectAnUnrelatedStack(
        string tags, bool arrayTags, bool nonWorkload)
    {
        JObject ignored = StackRow("contoso.ignored", tags);
        if (arrayTags)
        {
            ignored["tags"] = new JArray(tags.Split(' '));
        }

        if (nonWorkload)
        {
            ignored["packageTypes"] = new JArray("Dependency");
        }

        RawFeedClient client = NewClient((_, _) => Page(new JArray(
            ignored, StackRow("contoso.java", "kind:workload alias:java stack:java")), totalHits: 2));
        SetupStackCatalog discovery = new(NewCatalog(client));
        using CancellationTokenSource cancellation = new();

        SetupStackSnapshot snapshot = await discovery.GetStacksAsync(null, false, cancellation.Token);

        snapshot.IsFallback.Should().BeFalse();
        snapshot.StackNames.Should().Equal(["java"]);
        snapshot.StackPackageId("java").Should().Be("contoso.java");
        snapshot.StackPackageId("node").Should().BeNull();
        snapshot.AmbiguousAliases.Should().BeEmpty();
        client.Requests.Should().ContainSingle();
        client.RawCounts.Should().Equal([2]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task GetStacksAsync_RepeatedIdWithPortableAndConcreteRid_RestrictsOnlyAffectedRoleInEitherOrder(
        bool templates, bool reverseRows)
    {
        const string stackTags = "kind:workload alias:node alias:nodejs stack:node";
        const string templatesTags = "kind:content alias:node-templates";
        string tags = templates ? templatesTags : stackTags;
        string packageId = templates ? "contoso.templates" : "contoso.node";
        JObject first = StackRow(packageId, tags + (templates ? " rid:any" : string.Empty));
        JObject second = StackRow(packageId.ToUpperInvariant(), tags + " rid:linux-x64");
        if (reverseRows)
        {
            (first, second) = (second, first);
        }

        JArray rows = new(first, second,
            templates ? StackRow("contoso.node", stackTags) : StackRow("contoso.templates", templatesTags),
            StackRow("contoso.java", "kind:workload alias:java stack:java"));
        RawFeedClient client = NewClient((_, _) => Page(rows, totalHits: 4));
        SetupStackCatalog discovery = new(NewCatalog(client));
        using CancellationTokenSource cancellation = new();

        SetupStackSnapshot snapshot = await discovery.GetStacksAsync(null, false, cancellation.Token);

        string[] unsupportedAliases = templates ? ["node-templates"] : ["node", "nodejs"];
        string[] availableStacks = templates ? ["node", "java"] : ["java"];
        snapshot.IsFallback.Should().BeFalse();
        snapshot.UnsupportedAliases.Should().BeEquivalentTo(unsupportedAliases);
        snapshot.AmbiguousAliases.Should().BeEmpty();
        snapshot.StackPackageId("node").Should().Be(templates ? "contoso.node" : null);
        snapshot.StackPackageId("nodejs").Should().Be(templates ? "contoso.node" : null);
        snapshot.TemplatesPackageId("node").Should().BeNull();
        snapshot.TemplatesPackageId("nodejs").Should().BeNull();
        snapshot.StackNames.Should().BeEquivalentTo(availableStacks);
        snapshot.StackPackageId("java").Should().Be("contoso.java");

        SetupDependencyPlanBuilder planner = new(Substitute.For<IHostJsonBundleSectionReader>(), discovery);
        SetupCommandOptions options = new(new DirectoryInfo(Path.GetTempPath()), ["node", "java"], [], null,
            SetupInstallPolicy.LatestCompatible, false, NonInteractive: true, AssumeYes: true, Check: false, SetupOutputMode.Plain);
        SetupDependencyPlan plan = await planner.BuildDependencyPlanAsync(options,
            new SetupFeaturePlan(["node", "java"],
                [new SetupRuntimeFeature("node", "node", true), new SetupRuntimeFeature("java", "java", true)], ["node", "java"], false),
            SetupProfileScope.Unconstrained, cancellation.Token);

        plan.Failures.Should().ContainSingle().Which.Message.Should().Contain("node").And.Contain("RID-specific");
        plan.Dependencies.Should().NotContain(dependency => dependency.Name == "node");
        plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Stack
            && dependency.PackageId == "contoso.java");
        client.Requests.Should().ContainSingle();
        client.RawCounts.Should().Equal([4]);
    }

    [Theory]
    [InlineData("canonical", false)]
    [InlineData("canonical", true)]
    [InlineData("aliases", false)]
    [InlineData("aliases", true)]
    [InlineData("kind", false)]
    [InlineData("kind", true)]
    public async Task GetStacksAsync_SameIdHasContradictoryMetadataAcrossPages_RejectsEitherOrder(string disagreement, bool reversePages)
    {
        (string firstTags, string secondTags) = disagreement switch
        {
            "canonical" => (
                "kind:workload alias:node alias:javascript stack:node",
                "kind:workload alias:javascript alias:node stack:javascript"),
            "aliases" => (
                "kind:workload alias:node stack:node",
                "kind:workload alias:javascript alias:node stack:node"),
            "kind" => (
                "kind:workload alias:node stack:node",
                "kind:content alias:node stack:node"),
            _ => throw new ArgumentOutOfRangeException(nameof(disagreement)),
        };
        JObject first = StackRow("contoso.node", firstTags);
        JObject second = StackRow("CONTOSO.NODE", secondTags);
        if (reversePages)
        {
            (first, second) = (second, first);
        }

        JArray firstPage = new(Enumerable.Range(0, 100).Select(_ => FilteredRow()));
        firstPage[0] = first;
        RawFeedClient client = NewClient((skip, _) => skip switch
        {
            0 => Page(firstPage, totalHits: 101),
            100 => Page(new JArray(second), totalHits: 101),
            _ => throw new Xunit.Sdk.XunitException($"Unexpected feed offset {skip}."),
        });
        SetupStackCatalog discovery = new(NewCatalog(client));
        using CancellationTokenSource cancellation = new();

        await FluentActions.Awaiting(() => discovery.GetStacksAsync(null, false, cancellation.Token))
            .Should().ThrowExactlyAsync<SetupConfigurationException>();

        client.Requests.Select(uri => QueryInt(uri, "skip")).Should().Equal([0, 100]);
        client.RawCounts.Should().Equal([100, 1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetStacksAsync_ServerClampsPages_UsesRawOffsetsAndFindsTheLastClaimant(bool filteredFillers)
    {
        const string firstTags = "kind:workload alias:node alias:javascript stack:node";
        const string reorderedTags = "alias:javascript stack:node alias:node kind:workload";
        RawFeedClient client = NewClient((skip, _) =>
        {
            if (skip == 100)
            {
                return Page(new JArray(StackRow("second.node")), totalHits: 101);
            }

            if (skip is not (0 or 50))
            {
                throw new Xunit.Sdk.XunitException($"Unexpected feed offset {skip}.");
            }

            JArray rows = new(Enumerable.Range(0, 50).Select(index => filteredFillers
                ? FilteredRow()
                : StackRow("first.node", index % 2 == 0 ? firstTags : reorderedTags)));
            if (skip == 0)
            {
                rows[0] = StackRow("first.node", firstTags);
            }

            return Page(rows, totalHits: 101);
        });
        SetupStackCatalog discovery = new(NewCatalog(client));
        using CancellationTokenSource cancellation = new();

        SetupStackSnapshot snapshot = await discovery.GetStacksAsync(_source.Source, true, cancellation.Token);

        client.Requests.Select(uri => QueryInt(uri, "skip")).Should().Equal([0, 50, 100]);
        client.RawCounts.Should().Equal([50, 50, 1]);
        client.Requests.Should().OnlyContain(uri => QueryValue(uri, "q") == string.Empty
            && QueryValue(uri, "prerelease") == "true"
            && QueryValue(uri, "packageType") == "FuncCliWorkload");
        client.CancellationTokens.Should().OnlyContain(token => token == cancellation.Token);
        snapshot.IsAmbiguous("node").Should().BeTrue();
        snapshot.StackPackageId("node").Should().BeNull();
        snapshot.CanonicalStackName("javascript").Should().Be("node");
        snapshot.StackPackageId("javascript").Should().BeNull();
        snapshot.StackNames.Should().NotContain("node");
        snapshot.ConflictsFor("node").Should().ContainSingle()
            .Which.PackageIds.Should().Equal(["first.node", "second.node"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    public async Task GetStacksAsync_EmptyDataBeforeAdvertisedEnd_RejectsInsteadOfReturningAFallback(int emptyPageSkip)
    {
        RawFeedClient client = NewClient((skip, _) =>
        {
            if (skip == emptyPageSkip)
            {
                return Page([], totalHits: 101);
            }

            if (skip != 0)
            {
                throw new Xunit.Sdk.XunitException($"Unexpected feed offset {skip}.");
            }

            return Page(new JArray(Enumerable.Range(0, 50).Select(_ => StackRow("first.node"))), totalHits: 101);
        });
        SetupStackCatalog discovery = new(NewCatalog(client));
        using CancellationTokenSource cancellation = new();

        await FluentActions.Awaiting(() => discovery.GetStacksAsync(null, false, cancellation.Token))
            .Should().ThrowExactlyAsync<SetupConfigurationException>();

        int[] expectedSkips = emptyPageSkip == 0 ? [0] : [0, 50];
        client.Requests.Select(uri => QueryInt(uri, "skip")).Should().Equal(expectedSkips);
        client.RawCounts.Last().Should().Be(0);
    }

    [Theory]
    [InlineData(999)]
    [InlineData(1000)]
    [InlineData(1001)]
    public async Task GetStacksAsync_ClampedScanBoundary_CountsRawRowsAndNeverExceedsTheBudget(int totalHits)
    {
        const int rawRowLimit = 1000;
        const int serverPageLimit = 60;
        const int requestedPageSize = 100;
        RawFeedClient client = NewClient((skip, take) =>
        {
            if (skip >= rawRowLimit)
            {
                throw new Xunit.Sdk.XunitException($"Discovery requested offset {skip} beyond its raw-row budget.");
            }

            int count = Math.Min(Math.Min(serverPageLimit, take), totalHits - skip);
            JArray rows = new(Enumerable.Range(0, count).Select(offset => skip + offset == totalHits - 1
                ? StackRow("contoso.java", "kind:workload alias:java stack:java")
                : FilteredRow()));
            return Page(rows, totalHits);
        });
        SetupStackCatalog discovery = new(NewCatalog(client));
        using CancellationTokenSource cancellation = new();
        SetupStackSnapshot? snapshot = null;

        Exception? failure = await Record.ExceptionAsync(async () =>
        {
            snapshot = await discovery.GetStacksAsync(null, false, cancellation.Token);
        });

        if (totalHits > rawRowLimit)
        {
            failure.Should().BeOfType<SetupConfigurationException>();
            snapshot.Should().BeNull();
        }
        else
        {
            failure.Should().BeNull();
            snapshot.Should().NotBeNull();
            snapshot!.StackNames.Should().Equal(["java"]);
            snapshot!.StackPackageId("java").Should().Be("contoso.java");
        }

        int expectedRequests = (Math.Min(totalHits, rawRowLimit) + serverPageLimit - 1) / serverPageLimit;
        client.Requests.Select(uri => QueryInt(uri, "skip"))
            .Should().Equal(Enumerable.Range(0, expectedRequests).Select(index => index * serverPageLimit));
        client.Requests.Should().OnlyContain(uri => QueryInt(uri, "take") > 0
            && QueryInt(uri, "take") <= Math.Min(requestedPageSize, rawRowLimit - QueryInt(uri, "skip")));
        client.RawCounts.Sum().Should().Be(Math.Min(totalHits, rawRowLimit));
        client.RawCounts.Should().OnlyContain(count => count > 0 && count <= serverPageLimit);
        client.Requests.Zip(client.RawCounts).Should().OnlyContain(pair => pair.Second <= QueryInt(pair.First, "take"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchPageAsync_ValidStringPackageTypes_PreservesQueryAndSelectedSource(bool useOverride)
    {
        JObject workload = StackRow("CONTOSO.NODE", "kind:workload alias:node stack:node");
        workload["packageTypes"] = new JArray("Dependency", "funccliworkload");
        JObject unrelated = FilteredRow();
        unrelated["packageTypes"] = new JArray("Dependency");
        JObject response = Page(new JArray(workload, unrelated), totalHits: 39);
        RawFeedClient defaultClient = NewClient((_, _) => response);
        RawFeedClient overrideClient = NewClient((_, _) => response, _overrideSource);
        var provider = Substitute.For<IPackageSourceProvider>();
        provider.GetSource(null).Returns(_source);
        provider.GetSource(_overrideSource.Source).Returns(_overrideSource);
        List<PackageSource> selectedSources = [];
        WorkloadCatalog catalog = new(Options.Create(new WorkloadCatalogOptions { IncludePrerelease = true }), provider, source =>
        {
            selectedSources.Add(source);
            return source == _source ? defaultClient : overrideClient;
        });
        CatalogSearchQuery query = new()
        {
            Filter = "alias:node & name:C++",
            Source = useOverride ? _overrideSource.Source : null,
            Skip = 37,
            Take = 13,
        };
        using CancellationTokenSource cancellation = new();

        CatalogSearchPage page = await catalog.SearchPageAsync(query, cancellation.Token);

        RawFeedClient selectedClient = useOverride ? overrideClient : defaultClient;
        RawFeedClient unusedClient = useOverride ? defaultClient : overrideClient;
        CatalogSearchResult hit = page.Items.Should().ContainSingle().Subject;
        page.RawCount.Should().Be(2);
        hit.PackageId.Should().Be("contoso.node");
        hit.Source.Should().BeSameAs(selectedClient.Source);
        hit.CanonicalStack.Should().Be("node");
        selectedSources.Should().Equal([selectedClient.Source]);
        unusedClient.Requests.Should().BeEmpty();
        provider.Received(1).GetSource(query.Source);
        Uri uri = selectedClient.Requests.Should().ContainSingle().Subject;
        uri.Host.Should().Be(new Uri(selectedClient.Source.Source).Host);
        QueryValue(uri, "q").Should().Be(query.Filter);
        QueryInt(uri, "skip").Should().Be(37);
        QueryInt(uri, "take").Should().Be(13);
        QueryValue(uri, "prerelease").Should().Be("true");
        QueryValue(uri, "semVerLevel").Should().Be("2.0.0");
        QueryValue(uri, "packageType").Should().Be("FuncCliWorkload");
        selectedClient.CancellationTokens.Should().Equal([cancellation.Token]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParseV3Hits_ValidNormalMetadata_RemainsCompatible(bool arrayTags)
    {
        const string tags = "alias:javascript kind:workload stack:node alias:node";
        JObject row = StackRow("CONTOSO.NODE", tags);
        row["version"] = "1.2.3";
        row["title"] = "Node stack";
        row["description"] = "Node workload";
        if (arrayTags)
        {
            row["tags"] = new JArray(tags.Split(' '));
        }

        IReadOnlyList<CatalogSearchResult> results = NuGetProtocolSourceClient.ParseV3Hits(Page(new JArray(row), 1), _source);

        CatalogSearchResult hit = results.Should().ContainSingle().Subject;
        hit.PackageId.Should().Be("contoso.node");
        hit.LatestVersion.Should().Be(NuGetVersion.Parse("1.2.3"));
        hit.Title.Should().Be("Node stack");
        hit.Description.Should().Be("Node workload");
        hit.Aliases.Should().Equal(["javascript", "node"]);
        hit.CanonicalStack.Should().Be("node");
        hit.Kind.Should().Be("workload");
        hit.Source.Should().BeSameAs(_source);
    }

    private static JObject StackRow(string packageId, string tags = "kind:workload alias:node stack:node")
        => new()
        {
            ["id"] = packageId,
            ["version"] = "1.0.0",
            ["tags"] = tags,
            ["packageTypes"] = new JArray(new JObject { ["name"] = "FuncCliWorkload" }),
        };

    private static JObject FilteredRow()
        => new()
        {
            ["id"] = "unrelated.duplicate",
            ["version"] = "1.0.0",
            ["packageTypes"] = new JArray(new JObject { ["name"] = "Dependency" }),
        };

    private static JObject Page(JArray rows, int totalHits)
        => new() { ["data"] = rows, ["totalHits"] = totalHits };

    private static RawFeedClient NewClient(Func<int, int, JObject> response, PackageSource? source = null)
    {
        source ??= _source;
        JObject index = new()
        {
            ["version"] = "3.0.0",
            ["resources"] = new JArray(new JObject
            {
                ["@id"] = new Uri(new Uri(source.Source), "/query").AbsoluteUri,
                ["@type"] = "SearchQueryService/3.5.0",
            }),
        };
        ServiceIndexResourceV3 serviceIndex = new(index, DateTime.UnixEpoch);
        return new RawFeedClient(TestRepository.Build(source, serviceIndex), response);
    }

    private static WorkloadCatalog NewCatalog(RawFeedClient client)
    {
        var provider = Substitute.For<IPackageSourceProvider>();
        provider.GetSource(null).Returns(client.Source);
        provider.GetSource(client.Source.Source).Returns(client.Source);
        return new WorkloadCatalog(Options.Create(new WorkloadCatalogOptions()), provider, _ => client);
    }

    private static string QueryValue(Uri uri, string name)
        => Uri.UnescapeDataString(uri.Query.TrimStart('?').Split('&')
            .Single(part => part.StartsWith(name + "=", StringComparison.Ordinal))[(name.Length + 1)..]);

    private static int QueryInt(Uri uri, string name)
        => int.Parse(QueryValue(uri, name), CultureInfo.InvariantCulture);

    private sealed class RawFeedClient(SourceRepository repository, Func<int, int, JObject> response) : NuGetProtocolSourceClient(repository)
    {
        private readonly Func<int, int, JObject> _response = response ?? throw new ArgumentNullException(nameof(response));

        public List<Uri> Requests { get; } = [];

        public List<int> RawCounts { get; } = [];

        public List<CancellationToken> CancellationTokens { get; } = [];

        internal override Task<JObject?> FetchSearchResponseAsync(Uri searchUri, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(searchUri);
            CancellationTokens.Add(cancellationToken);
            JObject page = _response(QueryInt(searchUri, "skip"), QueryInt(searchUri, "take"));
            RawCounts.Add(((JArray)page["data"]!).Count);
            return Task.FromResult<JObject?>(page);
        }
    }
}
