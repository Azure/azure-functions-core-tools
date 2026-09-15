// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Workloads.Catalog;
using NSubstitute;
using NuGet.Configuration;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public class SetupStackCatalogTests
{
    private readonly IWorkloadCatalog _catalog = Substitute.For<IWorkloadCatalog>();

    [Fact]
    public async Task GetStacksAsync_DiscoversStacksFromKindWorkloadTag()
    {
        SinglePage(
                Result("azure.functions.cli.workloads.node", ["node"], kind: "workload"),
                Result("azure.functions.cli.workloads.java", ["java"], kind: "workload"),
                Result("azure.functions.cli.workloads.host", ["host"], kind: "content"),
                Result("azure.functions.cli.workloads.workers.node", ["node-worker"], kind: "content")
            );
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.StackNames.Should().BeEquivalentTo(["node", "java"]);
        snapshot.StackPackageId("java").Should().Be("azure.functions.cli.workloads.java");
        snapshot.SupportsStack("host").Should().BeFalse();
        snapshot.SupportsStack("node-worker").Should().BeFalse();
    }

    [Fact]
    public async Task GetStacksAsync_DiscoversJavaAndPowerShell_WhichTheBuiltInListOmits()
    {
        // The built-in fallback only knows node/python/go/dotnet, so java and
        // powershell stacks were skipped silently before catalog discovery.
        SetupDependency.BuiltInStackSnapshot.SupportsStack("java").Should().BeFalse();
        SetupDependency.BuiltInStackSnapshot.SupportsStack("powershell").Should().BeFalse();

        SinglePage(
                Result("azure.functions.cli.workloads.java", ["java"], kind: "workload"),
                Result("azure.functions.cli.workloads.powershell", ["powershell"], kind: "workload")
            );
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.SupportsStack("java").Should().BeTrue();
        snapshot.SupportsStack("powershell").Should().BeTrue();
    }

    [Fact]
    public async Task GetStacksAsync_MapsTemplatesAliasBackToStackName()
    {
        SinglePage(
                Result("azure.functions.cli.workloads.node", ["node"], kind: "workload"),
                Result("azure.functions.cli.workloads.templates.node", ["node-templates"], kind: "content")
            );
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.SupportsTemplates("node").Should().BeTrue();
        snapshot.TemplatesPackageId("node").Should().Be("azure.functions.cli.workloads.templates.node");
    }

    [Fact]
    public async Task GetStacksAsync_CatalogUnreachable_FallsBackToBuiltInList()
    {
        _catalog.SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<CatalogSearchResult>>(_ => throw new HttpRequestException("offline"));
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.StackNames.Should().BeEquivalentTo(SetupDependency.BuiltInStackSnapshot.StackNames);
    }

    [Fact]
    public async Task GetStacksAsync_EmptyCatalogResult_FallsBackToBuiltInList()
    {
        _catalog.SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.StackNames.Should().BeEquivalentTo(SetupDependency.BuiltInStackSnapshot.StackNames);
    }

    [Fact]
    public async Task GetStacksAsync_Cancellation_Propagates()
    {
        _catalog.SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<CatalogSearchResult>>(_ => throw new OperationCanceledException());
        SetupStackCatalog stackCatalog = new(_catalog);

        await FluentActions
            .Awaiting(() => stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetStacksAsync_CachesPerSourceAndPrereleaseCombination()
    {
        SinglePage(Result("azure.functions.cli.workloads.node", ["node"], kind: "workload"));
        SetupStackCatalog stackCatalog = new(_catalog);

        // Same key twice hits the cache; each distinct key discovers again.
        // One discovery costs two requests: the data page plus the empty page
        // that ends the walk.
        await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);
        await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);
        await stackCatalog.GetStacksAsync(source: null, includePrerelease: true, CancellationToken.None);
        await stackCatalog.GetStacksAsync(source: "https://other.test/v3/index.json", includePrerelease: false, CancellationToken.None);

        await _catalog.Received(6).SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetStacksAsync_ForwardsSourceAndPrereleaseToTheQuery()
    {
        const string source = "https://example.test/v3/index.json";
        SinglePage(Result("azure.functions.cli.workloads.node", ["node"], kind: "workload"));
        SetupStackCatalog stackCatalog = new(_catalog);

        await stackCatalog.GetStacksAsync(source, includePrerelease: true, CancellationToken.None);

        await _catalog.Received(1).SearchAsync(
            Arg.Is<CatalogSearchQuery>(q => q.Skip == 0 && q.Source == source && q.IncludePrerelease == true),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetStacksAsync_UnexpectedException_Propagates()
    {
        // Only transport/protocol failures fall back. A programming defect must
        // stay visible instead of being silently cached as "offline".
        _catalog.SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<CatalogSearchResult>>(_ => throw new NullReferenceException("bug"));
        SetupStackCatalog stackCatalog = new(_catalog);

        await FluentActions
            .Awaiting(() => stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None))
            .Should().ThrowAsync<NullReferenceException>();
    }

    [Fact]
    public async Task GetStacksAsync_FullFirstPage_KeepsPagingUntilEmpty()
    {
        // A full first page means there may be more; discovery must not stop at
        // the default page size and silently truncate the stack list.
        CatalogSearchResult[] fullPage = [.. Enumerable.Range(0, 100)
            .Select(i => Result($"azure.functions.cli.workloads.filler{i}", [$"filler{i}"], kind: "content"))];
        _catalog.SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _catalog.SearchAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 0), Arg.Any<CancellationToken>())
            .Returns(fullPage);
        _catalog.SearchAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 100), Arg.Any<CancellationToken>())
            .Returns([Result("azure.functions.cli.workloads.java", ["java"], kind: "workload")]);
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.SupportsStack("java").Should().BeTrue();
        await _catalog.Received(3).SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetStacksAsync_TwoPackagesClaimSameAlias_ExcludesItAsAmbiguous()
    {
        // Catalog ordering must not decide which package a stack alias installs.
        SinglePage(
                Result("azure.functions.cli.workloads.node", ["node"], kind: "workload"),
                Result("contoso.rogue.node", ["node"], kind: "workload"),
                Result("azure.functions.cli.workloads.python", ["python"], kind: "workload")
            );
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.IsAmbiguous("node").Should().BeTrue();
        snapshot.SupportsStack("node").Should().BeFalse();
        snapshot.StackPackageId("node").Should().BeNull();
        snapshot.SupportsStack("python").Should().BeTrue();
    }

    [Fact]
    public async Task GetStacksAsync_FallbackWithConflicts_DoesNotOfferTheContestedName()
    {
        // The fallback maps come from the built-in list, so a contested name is
        // still a key in them. Offering it would put a stack in the prompt that
        // planning is guaranteed to reject.
        SinglePage(
                Result("azure.functions.cli.workloads.node", ["node"], kind: "workload"),
                Result("contoso.rogue.node", ["node"], kind: "workload")
            );
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.IsAmbiguous("node").Should().BeTrue();
        snapshot.StackNames.Should().NotContain("node");
        snapshot.StackNames.Should().Contain("python", "stacks nobody contested stay on offer");
    }

    [Fact]
    public async Task GetStacksAsync_StackSquattingATemplatesAlias_LeavesTheRealTemplatesIntact()
    {
        // The templates map is only ever written by content packages, and it is
        // keyed by the stripped stack name while `ambiguous` holds raw aliases.
        // A stack package grabbing 'node-templates' therefore can't land in the
        // map at all, so the surviving entry is the legitimate package, not the
        // squatter's.
        SinglePage(
                Result("azure.functions.cli.workloads.templates.node", ["node-templates"], kind: "content"),
                Result("contoso.rogue.templates", ["node-templates"], kind: "workload"),
                Result("azure.functions.cli.workloads.node", ["node"], kind: "workload")
            );
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.IsAmbiguous("node-templates").Should().BeTrue();
        snapshot.StackNames.Should().Equal(["node"], "the squatter's primary alias is contested, so it is not a stack");
        snapshot.TemplatesPackageId("node").Should().Be(
            "azure.functions.cli.workloads.templates.node",
            "only content packages can write the templates map, so the squatter never reaches it");
    }

    [Fact]
    public async Task GetStacksAsync_TwoContentPackagesClaimOneTemplatesAlias_MarksTheStackContested()
    {
        // Conflicts between content packages are keyed by the stripped name, so
        // this is the case that must fail closed. It takes the whole stack with
        // it: 'node' is contested, so discovery yields no stacks at all and the
        // built-in list backs the snapshot. The contested name still can't be
        // offered or planned, which is the invariant that matters; whatever the
        // fallback happens to hold for it is never reached.
        SinglePage(
                Result("azure.functions.cli.workloads.templates.node", ["node-templates"], kind: "content"),
                Result("contoso.rogue.templates", ["node-templates"], kind: "content"),
                Result("azure.functions.cli.workloads.node", ["node"], kind: "workload")
            );
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.IsAmbiguous("node").Should().BeTrue();
        snapshot.IsAmbiguous("node-templates").Should().BeTrue();
        snapshot.StackNames.Should().NotContain("node");
    }

    [Fact]
    public async Task GetStacksAsync_StackReusingAContentAlias_IsAmbiguous()
    {
        // Alias ownership spans kinds. A stack grabbing the node worker's alias
        // would otherwise be offered and installed by exact id, skipping the
        // check that refuses the same alias on 'func workload install'.
        SinglePage(
                Result("azure.functions.cli.workloads.workers.node", ["node-worker"], kind: "content"),
                Result("contoso.rogue.worker", ["node-worker"], kind: "workload"),
                Result("azure.functions.cli.workloads.node", ["node"], kind: "workload")
            );
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.IsAmbiguous("node-worker").Should().BeTrue();
        snapshot.SupportsStack("node-worker").Should().BeFalse();
        snapshot.StackNames.Should().Equal(["node"], "the uncontested stack is unaffected");
    }

    [Fact]
    public async Task GetStacksAsync_ContentAliasesOfDistinctPackages_AreNotAmbiguous()
    {
        // The shipping layout gives every worker, templates, and host package
        // its own alias, so widening ownership must not invent conflicts.
        SinglePage(
                Result("azure.functions.cli.workloads.node", ["node"], kind: "workload"),
                Result("azure.functions.cli.workloads.workers.node", ["node-worker"], kind: "content"),
                Result("azure.functions.cli.workloads.templates.node", ["node-templates"], kind: "content"),
                Result("azure.functions.cli.workloads.host", ["host"], kind: "content")
            );
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.IsAmbiguous("node").Should().BeFalse();
        snapshot.IsAmbiguous("node-worker").Should().BeFalse();
        snapshot.IsAmbiguous("node-templates").Should().BeFalse();
        snapshot.StackNames.Should().Equal(["node"]);
        snapshot.TemplatesPackageId("node").Should().Be("azure.functions.cli.workloads.templates.node");
    }

    [Fact]
    public void Snapshot_ContestedName_ResolvesToNothingWithoutTheCallerAsking()
    {
        // The record must not be able to say both "node is contested" and
        // "node's package is X". On the fallback path the built-in maps still
        // hold an entry for a contested name, so the accessor has to refuse it
        // rather than leaving the invariant to whoever calls next.
        SetupStackSnapshot snapshot = new(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["node"] = "contoso.node", ["python"] = "contoso.python" },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["node"] = "contoso.templates.node" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "node" },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["nodejs"] = "node" });

        snapshot.StackPackageId("node").Should().BeNull();
        snapshot.TemplatesPackageId("node").Should().BeNull();
        snapshot.SupportsStack("node").Should().BeFalse();

        snapshot.StackPackageId("nodejs").Should().BeNull("an alternate folding onto a contested name is contested too");

        snapshot.StackPackageId("python").Should().Be("contoso.python", "uncontested names are unaffected");
    }

    [Fact]
    public async Task GetStacksAsync_UntaggedPackageClaimingAStackAlias_ContestsIt()
    {
        // A hit with no kind: tag isn't a stack, but it still claims its alias,
        // and WorkloadPackageSource would refuse that alias for the same reason.
        // Pinning the choice: ownership is decided by the alias, not the kind,
        // so an untagged package takes the name out of play rather than being
        // ignored and letting the alias resolve to whoever else wants it.
        SinglePage(
                Result("azure.functions.cli.workloads.node", ["node"], kind: "workload"),
                Result("contoso.untagged", ["node"], kind: null),
                Result("azure.functions.cli.workloads.python", ["python"], kind: "workload")
            );
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.IsAmbiguous("node").Should().BeTrue();
        snapshot.StackNames.Should().NotContain("node");
        snapshot.StackNames.Should().Contain("python");
    }

    [Fact]
    public async Task GetStacksAsync_ContestedPrimary_StillFoldsItsUncontestedAlternates()
    {
        // Dropping the package on a contested primary would also drop the
        // mapping for its alternates, and an alternate that no longer folds
        // stops looking like the contested stack at all.
        SinglePage(
                Result("azure.functions.cli.workloads.node", ["node", "nodejs"], kind: "workload"),
                Result("contoso.rogue.node", ["node"], kind: "workload")
            );
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.CanonicalStackName("nodejs").Should().Be("node");
        snapshot.IsAmbiguous("node").Should().BeTrue();
        snapshot.StackNames.Should().NotContain("node");
    }

    [Fact]
    public async Task GetStacksAsync_PackageWithSeveralAliases_OffersOnlyTheFirst()
    {
        // Interchangeable aliases describe one package, so offering both would
        // list the stack twice and let the secondary name drive worker and
        // templates lookups that only the primary one resolves.
        SinglePage(
                Result("azure.functions.cli.workloads.node", ["node", "nodejs"], kind: "workload"),
                Result("azure.functions.cli.workloads.templates.node", ["node-templates"], kind: "content")
            );
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.StackNames.Should().Equal(["node"]);
        snapshot.CanonicalStackName("nodejs").Should().Be("node");
        snapshot.SupportsStack("nodejs").Should().BeTrue("an explicit --features nodejs should still resolve");
        snapshot.StackPackageId("nodejs").Should().Be("azure.functions.cli.workloads.node");
        snapshot.TemplatesPackageId("nodejs").Should().Be(
            "azure.functions.cli.workloads.templates.node",
            "templates are keyed by the primary name, so the alternate has to fold onto it");
        snapshot.TemplatesPackageId("node").Should().Be("azure.functions.cli.workloads.templates.node");
    }

    [Fact]
    public async Task GetStacksAsync_SecondaryAliasContested_LeavesItUnresolved()
    {
        // A rogue package grabbing the alternate spelling shouldn't fold onto
        // the real stack, and shouldn't quietly resolve to itself either.
        SinglePage(
                Result("azure.functions.cli.workloads.node", ["node", "nodejs"], kind: "workload"),
                Result("contoso.rogue.nodejs", ["nodejs"], kind: "workload")
            );
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.IsAmbiguous("nodejs").Should().BeTrue();
        snapshot.CanonicalStackName("nodejs").Should().Be("nodejs");
        snapshot.SupportsStack("nodejs").Should().BeFalse();
        snapshot.StackNames.Should().Equal(["node"], "the primary name is uncontested");
    }

    [Fact]
    public async Task GetStacksAsync_PrimaryAliasAmbiguous_DropsItFromTheOffer()
    {
        // The conflict is on the name users would pick, so it can't stay in the
        // offer list just because a second package hasn't claimed the alternate.
        SinglePage(
                Result("azure.functions.cli.workloads.node", ["node", "nodejs"], kind: "workload"),
                Result("contoso.rogue.node", ["node"], kind: "workload"),
                Result("azure.functions.cli.workloads.python", ["python"], kind: "workload")
            );
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.IsAmbiguous("node").Should().BeTrue();
        snapshot.StackNames.Should().Equal(["python"]);
    }

    [Fact]
    public async Task GetStacksAsync_EveryAliasAmbiguous_KeepsTheConflictOnTheFallback()
    {
        // Removing every conflicting alias empties the map, which looks like
        // "the query returned nothing" and reaches for the built-in list. The
        // conflict has to survive that or the built-in id gets waved through
        // for the very alias a rogue package is fighting over.
        SinglePage(
                Result("azure.functions.cli.workloads.node", ["node"], kind: "workload"),
                Result("contoso.rogue.node", ["node"], kind: "workload")
            );
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.IsAmbiguous("node").Should().BeTrue();
        snapshot.StackNames.Should().NotBeEmpty("the built-in list still backs the unaffected stacks");
        snapshot.IsAmbiguous("python").Should().BeFalse();
        snapshot.SupportsStack("python").Should().BeTrue();
    }

    [Fact]
    public async Task GetStacksAsync_SameAliasSamePackageIdTwice_IsNotAmbiguous()
    {
        // Duplicate rows for one package (e.g. overlapping pages) are benign.
        SinglePage(
                Result("azure.functions.cli.workloads.node", ["node"], kind: "workload"),
                Result("azure.functions.cli.workloads.node", ["node"], kind: "workload")
            );
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.IsAmbiguous("node").Should().BeFalse();
        snapshot.StackPackageId("node").Should().Be("azure.functions.cli.workloads.node");
    }

    [Fact]
    public async Task GetStacksAsync_ShortPageOfFilteredHits_KeepsPaging()
    {
        // page.Count is post-filter, so a feed that ignores packageType can
        // return a full raw page that arrives here with only a few workloads.
        // Stopping on a short page would miss stacks on later pages.
        _catalog.SearchAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 0), Arg.Any<CancellationToken>())
            .Returns([Result("azure.functions.cli.workloads.node", ["node"], kind: "workload")]);
        _catalog.SearchAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 100), Arg.Any<CancellationToken>())
            .Returns([Result("azure.functions.cli.workloads.java", ["java"], kind: "workload")]);
        _catalog.SearchAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip >= 200), Arg.Any<CancellationToken>())
            .Returns([]);
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.SupportsStack("node").Should().BeTrue();
        snapshot.SupportsStack("java").Should().BeTrue();
    }

    /// <summary>
    /// Stubs a feed that returns everything on the first page and nothing after,
    /// which is what a real finite feed looks like to the paging loop.
    /// </summary>
    private void SinglePage(params CatalogSearchResult[] results)
    {
        _catalog.SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _catalog.SearchAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 0), Arg.Any<CancellationToken>())
            .Returns(results);
    }

    private static CatalogSearchResult Result(string packageId, string[] aliases, string? kind)
        => new(
            packageId,
            new NuGetVersion("1.0.0"),
            Title: null,
            Description: null,
            aliases,
            new PackageSource("https://example.test/v3/index.json"))
        {
            Kind = kind,
        };
}
