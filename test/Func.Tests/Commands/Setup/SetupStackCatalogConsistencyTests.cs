// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Storage;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public class SetupStackCatalogConsistencyTests
{
    [Theory]
    [InlineData(100L)]
    [InlineData(102L)]
    public async Task GetStacksAsync_TotalChanges_RejectsBeforeReadingAnotherPageAndDoesNotCache(long secondTotal)
    {
        var catalog = Substitute.For<IWorkloadCatalog>();
        var retry = false;
        List<int> offsets = [];
        catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                int skip = call.Arg<CatalogSearchQuery>().Skip;
                offsets.Add(skip);
                return retry
                    ? new CatalogSearchPage([Result("complete.java", ["java"])], 1, 1)
                    : new CatalogSearchPage([Result("partial.node", ["node"])], 50, skip == 0 ? 101 : secondTotal);
            });
        SetupStackCatalog discovery = new(catalog);

        await FluentActions.Awaiting(() => discovery.GetStacksAsync(null, false, CancellationToken.None))
            .Should().ThrowAsync<SetupConfigurationException>().WithMessage("*changed or omitted*totalHits*");

        offsets.Should().Equal([0, 50]);
        retry = true;
        SetupStackSnapshot recovered = await discovery.GetStacksAsync(null, false, CancellationToken.None);
        recovered.StackNames.Should().Equal(["java"]);
        offsets.Should().Equal([0, 50, 0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(50)]
    public async Task GetStacksAsync_TotalDisappears_RejectsEvenAnEmptyOrShortPage(int secondRawCount)
    {
        var catalog = Substitute.For<IWorkloadCatalog>();
        catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<CatalogSearchQuery>().Skip == 0
                ? new CatalogSearchPage([Result("partial.node", ["node"])], 50, 101)
                : new CatalogSearchPage([], secondRawCount));

        await FluentActions.Awaiting(() => new SetupStackCatalog(catalog).GetStacksAsync(null, false, CancellationToken.None))
            .Should().ThrowAsync<SetupConfigurationException>().WithMessage("*changed or omitted*totalHits*");

        await catalog.Received(2).SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetStacksAsync_TotalFirstAppearsLater_RetainsItUntilCompletion()
    {
        var catalog = Substitute.For<IWorkloadCatalog>();
        List<int> offsets = [];
        catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                int skip = call.Arg<CatalogSearchQuery>().Skip;
                offsets.Add(skip);
                return skip switch
                {
                    0 => new CatalogSearchPage([Result("contoso.node", ["node"])], 50),
                    50 => new CatalogSearchPage([], 50, 101),
                    100 => new CatalogSearchPage([Result("contoso.java", ["java"])], 1, 101),
                    _ => throw new InvalidOperationException("Unexpected offset."),
                };
            });

        SetupStackSnapshot snapshot = await new SetupStackCatalog(catalog).GetStacksAsync(null, false, CancellationToken.None);

        snapshot.StackNames.Should().BeEquivalentTo(["node", "java"]);
        offsets.Should().Equal([0, 50, 100]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task GetStacksAsync_ClampedPages_UsesRawOffsetsAndAllowsIdenticalParsedDuplicates(bool hasTotal, bool filteredMiddle)
    {
        var catalog = Substitute.For<IWorkloadCatalog>();
        var first = Result("first.node", ["node"]);
        CatalogSearchResult[] repeated = [.. Enumerable.Repeat(first, 50)];
        List<int> offsets = [];
        catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                int skip = call.Arg<CatalogSearchQuery>().Skip;
                offsets.Add(skip);
                long? total = hasTotal ? 101 : null;
                return skip switch
                {
                    0 => new CatalogSearchPage(repeated, 50, total),
                    50 => new CatalogSearchPage(filteredMiddle ? [] : repeated, 50, total),
                    100 => new CatalogSearchPage([Result("second.node", ["node"])], 1, total),
                    101 => new CatalogSearchPage([], 0),
                    _ => throw new InvalidOperationException("Unexpected offset."),
                };
            });

        SetupStackSnapshot snapshot = await new SetupStackCatalog(catalog).GetStacksAsync(null, false, CancellationToken.None);

        snapshot.IsAmbiguous("node").Should().BeTrue();
        snapshot.SupportsStack("node").Should().BeFalse();
        offsets.Should().Equal(hasTotal ? [0, 50, 100] : [0, 50, 100, 101]);
    }

    [Fact]
    public async Task GetStacksAsync_ZeroProgressBeforeKnownEnd_RejectsPagination()
    {
        var catalog = Substitute.For<IWorkloadCatalog>();
        catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<CatalogSearchQuery>().Skip == 0
                ? new CatalogSearchPage([Result("partial.node", ["node"])], 50, 101)
                : new CatalogSearchPage([], 0, 101));

        await FluentActions.Awaiting(() => new SetupStackCatalog(catalog).GetStacksAsync(null, false, CancellationToken.None))
            .Should().ThrowAsync<SetupConfigurationException>().WithMessage("*inconsistent pagination metadata*");

        await catalog.Received(2).SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetStacksAsync_TinyPages_StopAt32RequestsAndDoNotCache(bool hasTotal)
    {
        var catalog = Substitute.For<IWorkloadCatalog>();
        List<int> offsets = [];
        var retry = false;
        catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                int skip = call.Arg<CatalogSearchQuery>().Skip;
                offsets.Add(skip);
                return retry
                    ? new CatalogSearchPage([Result("complete.java", ["java"])], 1, 1)
                    : new CatalogSearchPage([Result($"contoso.stack{skip}", [$"stack{skip}"])], 1, hasTotal ? 40 : null);
            });
        SetupStackCatalog discovery = new(catalog);

        await FluentActions.Awaiting(() => discovery.GetStacksAsync(null, false, CancellationToken.None))
            .Should().ThrowAsync<SetupConfigurationException>().WithMessage("*scan limit*");

        offsets.Should().Equal(Enumerable.Range(0, 32));
        retry = true;
        SetupStackSnapshot recovered = await discovery.GetStacksAsync(null, false, CancellationToken.None);
        recovered.StackNames.Should().Equal(["java"]);
        offsets.Should().HaveCount(33);
        offsets.Last().Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetStacksAsync_EndOn32ndRequest_AcceptsCompletion(bool hasTotal)
    {
        var catalog = Substitute.For<IWorkloadCatalog>();
        catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => !hasTotal && call.Arg<CatalogSearchQuery>().Skip == 31
                ? new CatalogSearchPage([], 0)
                : new CatalogSearchPage([Result("contoso.node", ["node"])], 1, hasTotal ? 32 : null));

        SetupStackSnapshot snapshot = await new SetupStackCatalog(catalog).GetStacksAsync(null, false, CancellationToken.None);

        snapshot.StackNames.Should().Equal(["node"]);
        await catalog.Received(32).SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(999, false)]
    [InlineData(1000, false)]
    [InlineData(1000, true)]
    [InlineData(1001, true)]
    public async Task GetStacksAsync_RawRowBudget_RequiresProofOfCompletionWithin1000Rows(int rows, bool hasTotal)
    {
        var catalog = Substitute.For<IWorkloadCatalog>();
        List<(int Skip, int Take, int RawCount)> requests = [];
        catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var query = call.Arg<CatalogSearchQuery>();
                int take = query.Take!.Value;
                int rawCount = Math.Min(take, rows - query.Skip);
                requests.Add((query.Skip, take, rawCount));
                return new CatalogSearchPage(rawCount == 0 ? [] : [Result("contoso.node", ["node"])], rawCount, hasTotal ? rows : null);
            });
        SetupStackCatalog discovery = new(catalog);

        if (rows > 1000 || (rows == 1000 && !hasTotal))
        {
            await FluentActions.Awaiting(() => discovery.GetStacksAsync(null, false, CancellationToken.None))
                .Should().ThrowAsync<SetupConfigurationException>().WithMessage("*scan limit*");
        }
        else
        {
            SetupStackSnapshot snapshot = await discovery.GetStacksAsync(null, false, CancellationToken.None);
            snapshot.StackNames.Should().Equal(["node"]);
        }

        requests.Sum(request => request.RawCount).Should().Be(Math.Min(rows, 1000));
        requests.Should().OnlyContain(request => request.Skip + request.Take <= 1000 && request.RawCount <= request.Take);
        requests.Should().HaveCount(rows == 999 ? 11 : 10);
        if (rows == 999)
        {
            requests.Last().Should().Be((999, 1, 0));
        }
    }

    [Theory]
    [InlineData("io")]
    [InlineData("http")]
    [InlineData("protocol")]
    public async Task GetStacksAsync_CancelledTransport_PropagatesCancellationAndDoesNotCacheFallback(string failure)
    {
        Exception transport = failure switch
        {
            "io" => new IOException("cancelled transport"),
            "http" => new HttpRequestException("cancelled transport"),
            "protocol" => new FatalProtocolException("cancelled transport"),
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };
        using var cancellation = new CancellationTokenSource();
        var catalog = Substitute.For<IWorkloadCatalog>();
        catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call.Arg<CancellationToken>() == cancellation.Token)
                {
                    if (call.Arg<CatalogSearchQuery>().Skip == 0)
                    {
                        return new CatalogSearchPage([Result("partial.node", ["node"])], 100);
                    }

                    cancellation.Cancel();
                    throw transport;
                }

                return new CatalogSearchPage([Result("complete.java", ["java"])], 1, 1);
            });
        SetupStackCatalog discovery = new(catalog);

        var exception = await FluentActions.Awaiting(() => discovery.GetStacksAsync(null, false, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        exception.Which.CancellationToken.Should().Be(cancellation.Token);
        SetupStackSnapshot recovered = await discovery.GetStacksAsync(null, false, CancellationToken.None);
        recovered.StackNames.Should().Equal(["java"]);
        await catalog.Received(2).SearchPageAsync(Arg.Any<CatalogSearchQuery>(), cancellation.Token);
        await catalog.Received(1).SearchPageAsync(Arg.Any<CatalogSearchQuery>(), CancellationToken.None);
    }

    [Fact]
    public async Task GetStacksAsync_CancelledWhileAccumulatingFinalPage_DoesNotPublishSnapshotToCache()
    {
        using var cancellation = new CancellationTokenSource();
        var catalog = Substitute.For<IWorkloadCatalog>();
        catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<CancellationToken>() == cancellation.Token
                ? new CatalogSearchPage(new CancellingResults(cancellation, Result("partial.node", ["node"])), 1, 1)
                : new CatalogSearchPage([Result("complete.java", ["java"])], 1, 1));
        SetupStackCatalog discovery = new(catalog);

        await FluentActions.Awaiting(() => discovery.GetStacksAsync(null, false, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        SetupStackSnapshot recovered = await discovery.GetStacksAsync(null, false, CancellationToken.None);
        recovered.StackNames.Should().Equal(["java"]);
        await catalog.Received(2).SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("workload", false)]
    [InlineData("workload", true)]
    [InlineData("content", false)]
    [InlineData("content", true)]
    [InlineData("rid-pointer", false)]
    [InlineData("rid-pointer", true)]
    [InlineData("meta", false)]
    [InlineData("meta", true)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData("unknown", false)]
    [InlineData("unknown", true)]
    public async Task GetStacksAsync_ContestedRawTemplatesAlias_RefusesTemplatesOnLiveAndFallbackSnapshots(string? otherKind, bool offline)
    {
        var catalog = Substitute.For<IWorkloadCatalog>();
        catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<CatalogSearchQuery>().Skip == 0
                ? new CatalogSearchPage(
                    [Result("stack.node", ["node", "nodejs"], canonical: "node"),
                        Result("first.templates", ["node-templates"], "content"),
                        Result("second.claimant", ["node-templates"], otherKind)], 100)
                : offline ? throw new HttpRequestException("offline") : new CatalogSearchPage([], 0));

        SetupStackSnapshot snapshot = await new SetupStackCatalog(catalog).GetStacksAsync(null, false, CancellationToken.None);

        snapshot.IsAmbiguous("node-templates").Should().BeTrue();
        snapshot.TemplatesPackageId("node").Should().BeNull();
        snapshot.TemplatesPackageId("nodejs").Should().BeNull();
        snapshot.SupportsTemplates(" NODE ").Should().BeFalse();
        snapshot.ConflictsFor("node-templates").Should().ContainSingle().Which.PackageIds
            .Should().Equal(["first.templates", "second.claimant"]);
    }

    [Theory]
    [InlineData("node", false)]
    [InlineData("node", true)]
    [InlineData("node-templates", false)]
    [InlineData("node-templates", true)]
    [InlineData("node-worker", false)]
    [InlineData("node-worker", true)]
    [InlineData("host", false)]
    [InlineData("host", true)]
    public async Task GetStacksAsync_RidPointerOnlyFeed_RetainsObservedRoleOnCachedFallback(string alias, bool offline)
    {
        var catalog = Substitute.For<IWorkloadCatalog>();
        catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<CatalogSearchQuery>().Skip == 0
                ? new CatalogSearchPage([Result("contoso.pointer", [alias], "RID-POINTER")], 100)
                : offline ? throw new IOException("offline") : new CatalogSearchPage([], 0));
        SetupStackCatalog discovery = new(catalog);

        SetupStackSnapshot snapshot = await discovery.GetStacksAsync(null, false, CancellationToken.None);
        SetupStackSnapshot cached = await discovery.GetStacksAsync(null, false, CancellationToken.None);

        snapshot.UnsupportedAliases.Should().BeEquivalentTo([alias]);
        snapshot.IsUnsupported($" {alias.ToUpperInvariant()} ").Should().BeTrue();
        snapshot.IsAmbiguous(alias).Should().BeFalse();
        snapshot.SupportsStack("node").Should().Be(alias != "node");
        snapshot.SupportsTemplates("node").Should().Be(alias != "node" && alias != "node-templates");
        snapshot.SupportsStack("python").Should().BeTrue();
        snapshot.IsUnsupported("python").Should().BeFalse();
        cached.Should().BeSameAs(snapshot);
        await catalog.Received(2).SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false, "http")]
    [InlineData(false, "io")]
    [InlineData(false, "protocol")]
    [InlineData(true, "http")]
    [InlineData(true, "io")]
    [InlineData(true, "protocol")]
    public async Task Plan_TemplatesRestrictedBeforeTransportFailure_RejectsExplicitSecondaryAlias(bool conflict, string failure)
    {
        Exception transport = failure switch
        {
            "http" => new HttpRequestException("offline"),
            "io" => new IOException("offline"),
            "protocol" => new FatalProtocolException("offline"),
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };
        List<CatalogSearchResult> rows =
        [
            Result("stack.node", ["node", "nodejs"], canonical: "node"),
            Result("templates.node", ["node-templates"], conflict ? "content" : "rid-pointer"),
            Result("stack.java", ["java", "python"], canonical: "java"),
        ];
        if (conflict)
        {
            rows.Add(Result("other.claimant", ["node-templates"], "meta"));
        }

        var catalog = Substitute.For<IWorkloadCatalog>();
        catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<CatalogSearchQuery>().Skip == 0
                ? new CatalogSearchPage(rows, 100)
                : throw transport);
        SetupStackCatalog discovery = new(catalog);

        var (features, plan) = await ResolveAndBuildPlanAsync(discovery, "nodejs", CancellationToken.None);

        features.RuntimeFeatures.Should().ContainSingle().Which.Name.Should().Be("node");
        var error = plan.Failures.Should().ContainSingle().Which;
        error.Dependency.Name.Should().Be("node");
        error.Message.Should().Contain(conflict ? "workload-package alias collision" : "RID-specific");
        if (conflict)
        {
            error.Message.Should().Contain("node-templates").And.Contain("templates.node").And.Contain("other.claimant");
        }

        AssertNoRuntimeDependencies(plan);
        SetupStackSnapshot snapshot = await discovery.GetStacksAsync(null, false, CancellationToken.None);
        snapshot.CanonicalStackName(" NODEJS ").Should().Be("node");
        snapshot.IsAmbiguous("node").Should().BeFalse();
        snapshot.IsUnsupported("node").Should().BeFalse();
        snapshot.IsAmbiguous("node-templates").Should().Be(conflict);
        snapshot.IsUnsupported("node-templates").Should().Be(!conflict);
        snapshot.StackPackageId("nodejs").Should().Be(SetupDependency.BuiltInStackSnapshot.StackPackageId("node"));
        snapshot.TemplatesPackageId("nodejs").Should().BeNull();
        snapshot.CanonicalStackName("python").Should().Be("python");
        snapshot.StackPackageId("python").Should().Be(SetupDependency.BuiltInStackSnapshot.StackPackageId("python"));
        snapshot.SupportsStack("java").Should().BeFalse();
        await catalog.Received(2).SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
    }

    public static IEnumerable<object[]> ConcreteRids()
        => new[] { "win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64", "unrecognized-rid" }
            .Select(rid => new object[] { rid });

    public static IEnumerable<object[]> ConcreteRidRoles()
        => from arguments in ConcreteRids()
           from kind in new[] { "workload", "content" }
           from offline in new[] { false, true }
           from requested in new[] { "node", "nodejs" }
           select new object[] { arguments[0], kind, offline, requested };

    [Theory]
    [MemberData(nameof(ConcreteRidRoles))]
    public async Task Plan_ConcreteRidRoleWithoutPackageTypes_RejectsOnLiveAndFallbackSnapshots(
        string rid, string kind, bool offline, string requested)
    {
        bool isStack = kind == "workload";
        var hit = new JObject
        {
            ["id"] = isStack ? "stack.node" : "templates.node",
            ["version"] = "1.0.0",
            ["tags"] = $"kind:{kind} rid:{rid} " + (isStack
                ? "alias:node alias:nodejs stack:node"
                : "alias:node-templates alias:javascript-templates alias:node-assets"),
        };
        var parsed = NuGetProtocolSourceClient.ParseV3Hits(new JObject { ["data"] = new JArray(hit) },
            new PackageSource("https://example.test/v3/index.json")).Should().ContainSingle().Subject;
        parsed.Rid.Should().Be(rid);
        var catalog = Catalog(offline,
            isStack ? parsed : Result("stack.node", ["node", "nodejs"], canonical: "node"),
            isStack ? Result("templates.node", ["node-templates"], "content") : parsed,
            Result("stack.python", ["python"]));
        SetupStackCatalog discovery = new(catalog);

        var (features, plan) = await ResolveAndBuildPlanAsync(discovery, requested, CancellationToken.None);

        features.RuntimeFeatures.Should().ContainSingle().Which.Name.Should().Be("node");
        var error = plan.Failures.Should().ContainSingle().Which;
        error.Dependency.Name.Should().Be("node");
        error.Message.Should().Contain("RID-specific").And.Contain("portable stack and templates packages");
        AssertNoRuntimeDependencies(plan);
        SetupStackSnapshot snapshot = await discovery.GetStacksAsync(null, false, CancellationToken.None);
        snapshot.UnsupportedAliases.Should().BeEquivalentTo(parsed.Aliases);
        snapshot.CanonicalStackName("nodejs").Should().Be("node");
        snapshot.SupportsStack("nodejs").Should().Be(!isStack);
        snapshot.SupportsTemplates("nodejs").Should().BeFalse();
        snapshot.SupportsStack("python").Should().BeTrue();
        snapshot.IsUnsupported("python").Should().BeFalse();
        await catalog.Received(2).SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("any")]
    [InlineData("ANY")]
    [InlineData("aNy")]
    public async Task Plan_PortableStackAndTemplatesRids_PreservesCanonicalDependencies(string? rid)
    {
        var catalog = Catalog(false,
            Result("stack.node", ["node", "nodejs"], "WORKLOAD", "node", rid),
            Result("templates.node", ["node-templates"], "CONTENT", rid: rid));
        SetupStackCatalog discovery = new(catalog);

        var (features, plan) = await ResolveAndBuildPlanAsync(discovery, "nodejs", CancellationToken.None);

        features.RuntimeFeatures.Should().ContainSingle().Which.Name.Should().Be("node");
        AssertNodeDependencies(plan);
        SetupStackSnapshot snapshot = await discovery.GetStacksAsync(null, false, CancellationToken.None);
        snapshot.UnsupportedAliases.Should().BeEmpty();
        snapshot.StackPackageId("nodejs").Should().Be("stack.node");
        snapshot.TemplatesPackageId("nodejs").Should().Be("templates.node");
        await catalog.Received(2).SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(ConcreteRids))]
    public async Task Plan_ConcreteRidHostAndWorkerContent_DoesNotApplyPortableRoleRestrictions(string rid)
    {
        var catalog = Catalog(false,
            Result("stack.node", ["node", "nodejs"], canonical: "node"),
            Result("templates.node", ["node-templates"], "content"),
            Result("host", ["host"], "content", rid: rid),
            Result("worker.node", ["node-worker"], "content", rid: rid));
        SetupStackCatalog discovery = new(catalog);

        var (features, plan) = await ResolveAndBuildPlanAsync(discovery, "nodejs", CancellationToken.None);

        features.RuntimeFeatures.Should().ContainSingle().Which.Name.Should().Be("node");
        AssertNodeDependencies(plan);
        plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Host);
        SetupStackSnapshot snapshot = await discovery.GetStacksAsync(null, false, CancellationToken.None);
        snapshot.UnsupportedAliases.Should().BeEmpty();
        snapshot.StackNames.Should().Equal(["node"]);
        await catalog.Received(2).SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("content")]
    [InlineData("rid-pointer")]
    public async Task GetStacksAsync_HostAndWorkerRoles_DoNotDisableUnrelatedStackOrTemplates(string kind)
    {
        var catalog = Substitute.For<IWorkloadCatalog>();
        catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(new CatalogSearchPage(
                [Result("stack.node", ["node", "nodejs"], canonical: "node"),
                    Result("templates.node", ["node-templates"], "content"),
                    Result("host", ["host"], kind),
                    Result("worker.node", ["node-worker"], kind)], 4, 4));

        SetupStackSnapshot snapshot = await new SetupStackCatalog(catalog).GetStacksAsync(null, false, CancellationToken.None);

        snapshot.StackNames.Should().Equal(["node"]);
        snapshot.StackPackageId("nodejs").Should().Be("stack.node");
        snapshot.TemplatesPackageId("nodejs").Should().Be("templates.node");
        snapshot.IsUnsupported("node").Should().BeFalse();
        snapshot.IsUnsupported("node-templates").Should().BeFalse();
        snapshot.IsUnsupported("host").Should().Be(kind == "rid-pointer");
        snapshot.IsUnsupported("node-worker").Should().Be(kind == "rid-pointer");
    }

    [Theory]
    [InlineData("node")]
    [InlineData("nodejs")]
    [InlineData("node-templates")]
    [InlineData("node-worker")]
    [InlineData("host")]
    public void Snapshot_UnsupportedRoles_RefusesRawAndCanonicalAliasesWithoutPoisoningOtherRoles(string alias)
    {
        SetupStackSnapshot snapshot = new(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["node"] = "stack.node" },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["node"] = "templates.node" },
            SecondaryAliases: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["nodejs"] = "node" },
            UnsupportedAliases: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { alias });

        snapshot.SupportsStack("node").Should().Be(alias != "node");
        snapshot.SupportsStack(" nodeJS ").Should().Be(alias != "node" && alias != "nodejs");
        snapshot.SupportsTemplates("node").Should().Be(alias != "node" && alias != "node-templates");
        snapshot.SupportsTemplates(" nodeJS ").Should().Be(alias != "node" && alias != "nodejs" && alias != "node-templates");
        string[] expectedNames = alias == "node" ? [] : ["node"];
        snapshot.StackNames.Should().BeEquivalentTo(expectedNames);
    }

    private static IWorkloadCatalog Catalog(bool offline, params CatalogSearchResult[] rows)
    {
        var catalog = Substitute.For<IWorkloadCatalog>();
        catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<CatalogSearchQuery>().Skip == 0
                ? new CatalogSearchPage(rows, rows.Length)
                : offline ? throw new HttpRequestException("offline") : new CatalogSearchPage([], 0));
        return catalog;
    }

    private static async Task<(SetupFeaturePlan Features, SetupDependencyPlan Plan)> ResolveAndBuildPlanAsync(
        SetupStackCatalog discovery, string requested, CancellationToken cancellationToken)
    {
        SetupCommandOptions options = new(new DirectoryInfo(Path.GetTempPath()), [requested], [], null,
            SetupInstallPolicy.LatestCompatible, false, NonInteractive: true, AssumeYes: true, Check: true, SetupOutputMode.Json);
        SetupFeatureResolver resolver = new(new TestInteractionService(), Substitute.For<IWorkloadStore>(),
            Substitute.For<ICliConfigurationProvider>(), discovery);
        SetupDependencyPlanBuilder planner = new(Substitute.For<IHostJsonBundleSectionReader>(), discovery);
        SetupFeaturePlan? features = await resolver.ResolveFeaturesAsync(options, cancellationToken);
        features.Should().NotBeNull();
        SetupDependencyPlan plan = await planner.BuildDependencyPlanAsync(options, features!, SetupProfileScope.Unconstrained, cancellationToken);
        return (features!, plan);
    }

    private static void AssertNoRuntimeDependencies(SetupDependencyPlan plan)
        => plan.Dependencies.Should().NotContain(dependency => dependency.Kind == SetupDependencyKind.Worker
            || dependency.Kind == SetupDependencyKind.Stack || dependency.Kind == SetupDependencyKind.Templates);

    private static void AssertNodeDependencies(SetupDependencyPlan plan)
    {
        plan.Failures.Should().BeEmpty();
        plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Stack)
            .Which.PackageId.Should().Be("stack.node");
        plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Templates)
            .Which.PackageId.Should().Be("templates.node");
        plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Worker)
            .Which.Name.Should().Be("node");
    }

    private static CatalogSearchResult Result(string id, string[] aliases, string? kind = "workload", string? canonical = null, string? rid = null)
        => new(id, new NuGetVersion("1.0.0"), null, null, aliases, new PackageSource("https://example.test/v3/index.json"))
        {
            Kind = kind,
            CanonicalStack = canonical,
            Rid = rid,
        };

    private sealed class CancellingResults(CancellationTokenSource cancellation, CatalogSearchResult result) : IReadOnlyList<CatalogSearchResult>
    {
        public int Count => 1;

        public CatalogSearchResult this[int index] => index == 0 ? result : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<CatalogSearchResult> GetEnumerator()
        {
            cancellation.Cancel();
            yield return result;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}