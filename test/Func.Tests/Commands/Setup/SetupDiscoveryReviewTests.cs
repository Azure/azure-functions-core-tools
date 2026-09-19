// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads.Catalog;
using NSubstitute;
using NuGet.Configuration;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public class SetupDiscoveryReviewTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task Plan_ConflictDiagnostic_ListsActualAliasesAndClaimingPackages(bool templates, bool pageFails, bool includeUncontestedStack)
    {
        string alias = templates ? "node-templates" : "node";
        CatalogSearchResult[] rows =
        [
            Result("stack.node", ["node", "nodejs"], canonical: "node"),
            Result("first.claimant", [alias], "content"),
            Result("second.claimant", [alias], "content"),
        ];
        if (includeUncontestedStack) rows = [.. rows.Reverse(), Result("contoso.java", ["java"])];
        var catalog = Substitute.For<IWorkloadCatalog>();
        catalog.SearchPageAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 0), Arg.Any<CancellationToken>())
            .Returns(new CatalogSearchPage(rows, 100));
        catalog.SearchPageAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 100), Arg.Any<CancellationToken>())
            .Returns<CatalogSearchPage>(_ => pageFails ? throw new HttpRequestException("offline") : new CatalogSearchPage([], 0));
        var discovery = new SetupStackCatalog(catalog);
        SetupStackSnapshot snapshot = await discovery.GetStacksAsync(null, false, CancellationToken.None);
        snapshot.SupportsStack("java").Should().Be(includeUncontestedStack && !pageFails);
        SetupAliasConflict conflict = snapshot.ConflictsFor("nodejs").Should().ContainSingle().Subject;
        conflict.Alias.Should().Be(alias);
        conflict.PackageIds.Should().Equal(templates
            ? ["first.claimant", "second.claimant"]
            : ["first.claimant", "second.claimant", "stack.node"]);
        string canonical = snapshot.CanonicalStackName("nodejs");
        var builder = new SetupDependencyPlanBuilder(Substitute.For<IHostJsonBundleSectionReader>(), discovery);

        SetupDependencyPlan plan = await builder.BuildDependencyPlanAsync(Options(),
            new SetupFeaturePlan([canonical], [new SetupRuntimeFeature(canonical, canonical, true)], [canonical], false),
            SetupProfileScope.Unconstrained, CancellationToken.None);

        string message = plan.Failures.Should().ContainSingle().Which.Message;
        message.Should().Contain(alias).And.Contain("first.claimant").And.Contain("second.claimant").And.Contain("--exact");
        if (!templates) message.Should().Contain("stack.node");
        plan.Dependencies.Should().NotContain(dependency => dependency.Kind == SetupDependencyKind.Worker
            || dependency.Kind == SetupDependencyKind.Stack || dependency.Kind == SetupDependencyKind.Templates);
    }

    [Fact]
    public async Task Discovery_CachedSnapshot_CancellationStillPropagates()
    {
        var catalog = Catalog(Result("contoso.node", ["node"]));
        SetupStackCatalog discovery = new(catalog);
        await discovery.GetStacksAsync(null, false, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await FluentActions.Awaiting(() => discovery.GetStacksAsync(null, false, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        await catalog.Received(2).SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
        await catalog.Received(1).SearchPageAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 1), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public async Task Discovery_LastAllowedPageReachesEnd_DoesNotReportLimitFailure(int finalRawCount)
    {
        var catalog = Substitute.For<IWorkloadCatalog>();
        long? totalHits = finalRawCount == 0 ? null : 900 + finalRawCount;
        catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(new CatalogSearchPage([Result("contoso.node", ["node"])], 100, TotalHits: totalHits));
        catalog.SearchPageAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 900), Arg.Any<CancellationToken>())
            .Returns(new CatalogSearchPage([], finalRawCount, TotalHits: totalHits));

        SetupStackSnapshot snapshot = await new SetupStackCatalog(catalog).GetStacksAsync(null, false, CancellationToken.None);

        snapshot.StackNames.Should().Equal(["node"]);
        await catalog.Received(10).SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
        await catalog.Received(1).SearchPageAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 900 && q.Take == 100), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Discovery_MalformedPage_IsReportedAsSetupFailureInJsonMode()
    {
        var catalog = Substitute.For<IWorkloadCatalog>();
        catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns<CatalogSearchPage>(_ => throw new InvalidDataException("invalid data array"));
        SetupStackCatalog discovery = new(catalog);
        var features = Substitute.For<ISetupFeatureResolver>();
        features.ResolveFeaturesAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await discovery.GetStacksAsync(null, false, CancellationToken.None);
                return (SetupFeaturePlan?)new SetupFeaturePlan(["node"], [], [], false);
            });
        var interaction = new TestInteractionService();
        var installer = Substitute.For<ISetupDependencyInstaller>();
        SetupRunner runner = new(interaction, features, Substitute.For<ISetupProfileScopeResolver>(),
            Substitute.For<ISetupDependencyPlanBuilder>(), installer);

        SetupRunResult result = await runner.RunAsync(Options() with { OutputMode = SetupOutputMode.Json }, CancellationToken.None);

        result.ExitCode.Should().Be(1);
        interaction.AllOutput.Should().Contain("setup.failed").And.Contain("invalid data array");
        await installer.DidNotReceiveWithAnyArgs().EnsureDependencyAsync(default!, default!, default);
    }

    [Fact]
    public async Task Discovery_RepeatedSameAliasWithoutCanonicalTag_RemainsUnambiguous()
    {
        var catalog = Catalog(Result("contoso.node", ["node", "NODE"]));

        SetupStackSnapshot snapshot = await new SetupStackCatalog(catalog).GetStacksAsync(null, false, CancellationToken.None);

        snapshot.StackNames.Should().Equal(["node"]);
        snapshot.StackPackageId("node").Should().Be("contoso.node");
    }

    [Fact]
    public async Task Discovery_FullyFilteredRawPage_ContinuesAndDetectsLaterConflict()
    {
        var catalog = Substitute.For<IWorkloadCatalog>();
        catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(new CatalogSearchPage([], 0));
        catalog.SearchPageAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 0), Arg.Any<CancellationToken>())
            .Returns(new CatalogSearchPage([Result("first.node", ["node"])], 100));
        catalog.SearchPageAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 100), Arg.Any<CancellationToken>())
            .Returns(new CatalogSearchPage([], 100));
        catalog.SearchPageAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 200), Arg.Any<CancellationToken>())
            .Returns(new CatalogSearchPage([Result("second.node", ["node"])], 1));
        SetupStackCatalog discovery = new(catalog);

        SetupStackSnapshot snapshot = await discovery.GetStacksAsync(null, false, CancellationToken.None);

        snapshot.IsAmbiguous("node").Should().BeTrue();
        snapshot.SupportsStack("node").Should().BeFalse();
        await catalog.Received(4).SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
        await catalog.Received(1).SearchPageAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 201), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Discovery_ScanLimit_StopsBeforePlanningAndDoesNotCachePartialResults()
    {
        var catalog = Substitute.For<IWorkloadCatalog>();
        catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(new CatalogSearchPage([Result("partial.node", ["node"])], 100));
        SetupStackCatalog discovery = new(catalog);
        var plans = Substitute.For<ISetupDependencyPlanBuilder>();
        var installer = Substitute.For<ISetupDependencyInstaller>();
        var features = Substitute.For<ISetupFeatureResolver>();
        features.ResolveFeaturesAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await discovery.GetStacksAsync(null, false, CancellationToken.None);
                return (SetupFeaturePlan?)new SetupFeaturePlan(["node"], [], [], false);
            });
        SetupRunner runner = new(new TestInteractionService(), features,
            Substitute.For<ISetupProfileScopeResolver>(), plans, installer);

        SetupRunResult result = await runner.RunAsync(Options(), CancellationToken.None);

        result.ExitCode.Should().Be(1);
        await plans.DidNotReceiveWithAnyArgs().BuildDependencyPlanAsync(default!, default!, default!, default);
        await installer.DidNotReceiveWithAnyArgs().EnsureDependencyAsync(default!, default!, default);
        await catalog.Received(10).SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
        catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(new CatalogSearchPage([Result("complete.python", ["python"])], 1, TotalHits: 1));
        SetupStackSnapshot retried = await discovery.GetStacksAsync(null, false, CancellationToken.None);
        SetupStackSnapshot cached = await discovery.GetStacksAsync(null, false, CancellationToken.None);

        retried.StackNames.Should().Equal(["python"]);
        cached.Should().BeSameAs(retried);
        await catalog.Received(11).SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
        await catalog.Received(2).SearchPageAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 0), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown")]
    [InlineData("node javascript")]
    public async Task Discovery_MultipleAliasesWithoutValidCanonicalStack_RefusesToGuess(string? canonical)
    {
        var catalog = Catalog(Result("contoso.node", ["javascript", "node"], canonical: canonical));

        await FluentActions.Awaiting(() => new SetupStackCatalog(catalog).GetStacksAsync(null, false, CancellationToken.None))
            .Should().ThrowAsync<SetupConfigurationException>().WithMessage("*stack:*canonical*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discovery_ExplicitCanonicalStack_DoesNotDependOnAliasOrder(bool reverse)
    {
        string[] aliases = reverse ? ["node", "javascript"] : ["javascript", "node"];
        var catalog = Catalog(Result("contoso.node", aliases, canonical: "node"));

        SetupStackSnapshot snapshot = await new SetupStackCatalog(catalog).GetStacksAsync(null, false, CancellationToken.None);

        snapshot.StackNames.Should().Equal(["node"]);
        snapshot.CanonicalStackName("javascript").Should().Be("node");
        snapshot.StackPackageId("javascript").Should().Be("contoso.node");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("meta")]
    [InlineData("rid-pointer")]
    [InlineData("unknown")]
    public async Task Discovery_NonContentTemplatesAlias_IsNotATemplatesPackage(string? kind)
    {
        var catalog = Catalog(Result("contoso.node", ["node"]),
            Result("contoso.not-content", ["node-templates"], kind));

        SetupStackSnapshot snapshot = await new SetupStackCatalog(catalog).GetStacksAsync(null, false, CancellationToken.None);

        snapshot.TemplatesPackageId("node").Should().BeNull();
        snapshot.StackNames.Should().Equal(["node"]);
    }

    [Fact]
    public async Task Plan_TemplateConflict_DiagnosticDoesNotClaimOnlyTheStackAliasCollided()
    {
        var catalog = Catalog(Result("contoso.node", ["node"]),
            Result("first.templates", ["node-templates"], "content"),
            Result("second.templates", ["node-templates"], "content"));
        SetupStackCatalog discovery = new(catalog);
        SetupDependencyPlanBuilder builder = new(Substitute.For<IHostJsonBundleSectionReader>(), discovery);

        SetupDependencyPlan plan = await builder.BuildDependencyPlanAsync(Options(),
            new SetupFeaturePlan(["node"], [new SetupRuntimeFeature("node", "node", true)], ["node"], false),
            SetupProfileScope.Unconstrained, CancellationToken.None);

        plan.Failures.Should().ContainSingle().Which.Message.Should().Contain("workload-package alias collision");
        plan.Dependencies.Should().NotContain(dependency => dependency.Kind == SetupDependencyKind.Stack
            || dependency.Kind == SetupDependencyKind.Worker || dependency.Kind == SetupDependencyKind.Templates);
    }

    [Theory]
    [InlineData("content")]
    [InlineData("rid-pointer")]
    [InlineData("meta")]
    [InlineData(null)]
    public async Task Plan_CrossKindConflict_ReportsGenericWorkloadAliasCollision(string? kind)
    {
        var catalog = Catalog(Result("contoso.stack", ["node-worker"]), Result("contoso.other", ["node-worker"], kind));
        var builder = new SetupDependencyPlanBuilder(Substitute.For<IHostJsonBundleSectionReader>(), new SetupStackCatalog(catalog));

        SetupDependencyPlan plan = await builder.BuildDependencyPlanAsync(Options(),
            new SetupFeaturePlan(["node-worker"], [new SetupRuntimeFeature("node-worker", "node-worker", true)], ["node-worker"], false),
            SetupProfileScope.Unconstrained, CancellationToken.None);

        plan.Failures.Should().ContainSingle().Which.Message.Should().Contain("workload-package alias collision")
            .And.Contain("node-worker").And.NotContain("stack or templates aliases");
        plan.Dependencies.Should().NotContain(dependency => dependency.Kind == SetupDependencyKind.Stack
            || dependency.Kind == SetupDependencyKind.Worker || dependency.Kind == SetupDependencyKind.Templates);
    }

    private static IWorkloadCatalog Catalog(params CatalogSearchResult[] items)
    {
        var catalog = Substitute.For<IWorkloadCatalog>();
        catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(new CatalogSearchPage([], 0));
        catalog.SearchPageAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 0), Arg.Any<CancellationToken>())
            .Returns(new CatalogSearchPage(items, items.Length));
        return catalog;
    }

    private static CatalogSearchResult Result(string id, string[] aliases, string? kind = "workload", string? canonical = null)
        => new(id, new NuGetVersion("1.0.0"), null, null, aliases, new PackageSource("https://example.test/v3/index.json"))
        {
            Kind = kind,
            CanonicalStack = canonical,
        };

    private static SetupCommandOptions Options()
        => new(new DirectoryInfo(Path.GetTempPath()), ["node"], [], null, SetupInstallPolicy.LatestCompatible,
            false, NonInteractive: true, AssumeYes: true, Check: false, SetupOutputMode.Plain);
}