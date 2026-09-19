// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

/// <summary>
/// Covers the wiring between catalog-discovered stacks and the two consumers,
/// which the runner-level tests can't reach because their fake catalog always
/// falls back to the built-in list.
/// </summary>
public class SetupStackDiscoveryWiringTests
{
    private readonly ISetupStackCatalog _stackCatalog = Substitute.For<ISetupStackCatalog>();
    private readonly IHostJsonBundleSectionReader _bundleReader = Substitute.For<IHostJsonBundleSectionReader>();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FeatureResolver_FallbackWithContestedPrimary_DoesNotOfferItsBuiltInAlternate(bool pageFails)
    {
        var catalog = Substitute.For<IWorkloadCatalog>();
        var source = new NuGet.Configuration.PackageSource("https://example.test/v3/index.json");
        catalog.SearchPageAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 0), Arg.Any<CancellationToken>())
            .Returns(Page(
                new CatalogSearchResult("contoso.node", new NuGetVersion("1.0.0"), null, null, ["node", "python"], source)
                {
                    Kind = "workload",
                    CanonicalStack = "node",
                },
                new CatalogSearchResult("contoso.other-node", new NuGetVersion("1.0.0"), null, null, ["node"], source)
                {
                    Kind = "content",
                }));
        catalog.SearchPageAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 100), Arg.Any<CancellationToken>())
            .Returns<CatalogSearchPage>(_ => pageFails ? throw new HttpRequestException("offline") : Page());
        SetupStackCatalog stacks = new(catalog);
        IWorkloadStore store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);
        ICliConfigurationProvider configuration = Substitute.For<ICliConfigurationProvider>();
        configuration.GetProjectConfiguration(Arg.Any<DirectoryInfo>()).Returns(new ConfigurationBuilder().Build());
        SelectAllInteractionService interaction = new();
        SetupFeatureResolver resolver = new(interaction, store, configuration, stacks);
        SetupDependencyPlanBuilder builder = new(_bundleReader, stacks);

        SetupFeaturePlan? features = await resolver.ResolveFeaturesAsync(Options([]), CancellationToken.None);
        SetupDependencyPlan plan = await builder.BuildDependencyPlanAsync(
            Options([]), features!, SetupProfileScope.Unconstrained, CancellationToken.None);

        interaction.MultiSelectionChoices.Should().ContainSingle().Which.Select(choice => choice.Value)
            .Should().BeEquivalentTo(["dotnet", "go"]);
        features!.Features.Should().BeEquivalentTo(["dotnet", "go"]);
        plan.Failures.Should().BeEmpty();
        plan.Dependencies.Where(dependency => dependency.Kind == SetupDependencyKind.Stack)
            .Select(dependency => dependency.Name).Should().BeEquivalentTo(["dotnet", "go"]);
        await catalog.Received(2).SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("host", true)]
    [InlineData("runtime", true)]
    [InlineData(".net", true)]
    [InlineData("dotnet-inprocess", true)]
    [InlineData("dotnet-isolated", true)]
    [InlineData("host", false)]
    [InlineData("runtime", false)]
    [InlineData(".net", false)]
    [InlineData("dotnet-inprocess", false)]
    [InlineData("dotnet-isolated", false)]
    public async Task FeatureResolver_AlternateOfReservedPrimary_RefusesInsteadOfDispatchingBuiltInFeature(string primary, bool pageFails)
    {
        var catalog = Substitute.For<IWorkloadCatalog>();
        var source = new NuGet.Configuration.PackageSource("https://example.test/v3/index.json");
        catalog.SearchPageAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 0), Arg.Any<CancellationToken>())
            .Returns(Page(
                new CatalogSearchResult("contoso.stack", new NuGetVersion("1.0.0"), null, null, [primary, "node"], source)
                {
                    Kind = "workload",
                    CanonicalStack = primary,
                },
                new CatalogSearchResult("contoso.conflict", new NuGetVersion("1.0.0"), null, null, [primary], source)
                {
                    Kind = "content",
                }));
        catalog.SearchPageAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 100), Arg.Any<CancellationToken>())
            .Returns<CatalogSearchPage>(_ => pageFails ? throw new HttpRequestException("offline") : Page());
        SetupStackCatalog stacks = new(catalog);
        SetupFeatureResolver resolver = new(new TestInteractionService(), Substitute.For<IWorkloadStore>(),
            Substitute.For<ICliConfigurationProvider>(), stacks);

        await FluentActions.Awaiting(() => resolver.ResolveFeaturesAsync(Options(["node"]), CancellationToken.None))
            .Should().ThrowAsync<SetupConfigurationException>().WithMessage("*reserved setup feature*");
    }

    [Theory]
    [InlineData("node")]
    [InlineData("nodejs")]
    public async Task Discovery_LaterPageFails_RefusesTheObservedContestedStackAndItsAlternate(string feature)
    {
        var catalog = Substitute.For<IWorkloadCatalog>();
        var source = new NuGet.Configuration.PackageSource("https://example.test/v3/index.json");
        catalog.SearchPageAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 0), Arg.Any<CancellationToken>())
            .Returns(Page(
                new CatalogSearchResult("contoso.node", new NuGetVersion("1.0.0"), null, null, ["node", "nodejs"], source)
                {
                    Kind = "workload",
                    CanonicalStack = "node",
                },
                new CatalogSearchResult("contoso.other-node", new NuGetVersion("1.0.0"), null, null, ["node"], source)
                {
                    Kind = "content",
                }));
        catalog.SearchPageAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 100), Arg.Any<CancellationToken>())
            .Returns<CatalogSearchPage>(_ => throw new HttpRequestException("offline"));
        SetupStackCatalog stacks = new(catalog);
        SetupFeatureResolver resolver = new(new TestInteractionService(), Substitute.For<IWorkloadStore>(),
            Substitute.For<ICliConfigurationProvider>(), stacks);
        SetupDependencyPlanBuilder builder = new(_bundleReader, stacks);

        SetupFeaturePlan? features = await resolver.ResolveFeaturesAsync(Options([feature]), CancellationToken.None);
        SetupDependencyPlan plan = await builder.BuildDependencyPlanAsync(
            Options([feature]), features!, SetupProfileScope.Unconstrained, CancellationToken.None);

        features!.Features.Should().Equal(["node"]);
        plan.Failures.Should().ContainSingle().Which.Message.Should().Contain("More than one workload package");
        plan.Dependencies.Should().NotContain(d =>
            d.Kind == SetupDependencyKind.Worker || d.Kind == SetupDependencyKind.Stack || d.Kind == SetupDependencyKind.Templates);
        await catalog.Received(2).SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlanBuilder_UsesDiscoveredPackageId_NotTheBuiltInOne()
    {
        // A stack the built-in list doesn't know about must still be planned,
        // with the package id the catalog reported.
        const string discoveredId = "contoso.functions.cli.workloads.java";
        SetupDependency.BuiltInStackSnapshot.SupportsStack("java").Should().BeFalse();
        WithDiscoveredStacks(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["java"] = discoveredId,
        });
        SetupDependencyPlanBuilder builder = new(_bundleReader, _stackCatalog);

        SetupDependencyPlan plan = await builder.BuildDependencyPlanAsync(
            Options(["java"]),
            FeaturePlan("java"),
            SetupProfileScope.Unconstrained,
            CancellationToken.None);

        SetupDependency stack = plan.Dependencies.Should()
            .ContainSingle(d => d.Kind == SetupDependencyKind.Stack).Subject;
        stack.Name.Should().Be("java");
        stack.PackageId.Should().Be(discoveredId);
    }

    [Fact]
    public async Task PlanBuilder_UsesDiscoveredTemplatesPackageId()
    {
        const string templatesId = "contoso.functions.cli.workloads.templates.node";
        WithDiscoveredStacks(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["node"] = "contoso.node" },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["node"] = templatesId });
        SetupDependencyPlanBuilder builder = new(_bundleReader, _stackCatalog);

        SetupDependencyPlan plan = await builder.BuildDependencyPlanAsync(
            Options(["node"]),
            FeaturePlan("node"),
            SetupProfileScope.Unconstrained,
            CancellationToken.None);

        plan.Dependencies.Should()
            .ContainSingle(d => d.Kind == SetupDependencyKind.Templates)
            .Which.PackageId.Should().Be(templatesId);
    }

    [Fact]
    public async Task PlanBuilder_StackNotPublished_PlansNoStackDependency()
    {
        WithDiscoveredStacks(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["node"] = "azure.functions.cli.workloads.node",
        });
        SetupDependencyPlanBuilder builder = new(_bundleReader, _stackCatalog);

        SetupDependencyPlan plan = await builder.BuildDependencyPlanAsync(
            Options(["ruby"]),
            FeaturePlan("ruby"),
            SetupProfileScope.Unconstrained,
            CancellationToken.None);

        plan.Dependencies.Should().NotContain(d => d.Kind == SetupDependencyKind.Stack);
    }

    [Fact]
    public async Task PlanBuilder_HostOnly_NeverAsksTheCatalog()
    {
        // `func setup --features host` must stay a zero-network plan.
        SetupDependencyPlanBuilder builder = new(_bundleReader, _stackCatalog);

        await builder.BuildDependencyPlanAsync(
            Options(["host"]),
            new SetupFeaturePlan(["host"], [], [], IncludeExtensionBundle: false),
            SetupProfileScope.Unconstrained,
            CancellationToken.None);

        await _stackCatalog.DidNotReceive().GetStacksAsync(
            Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlanBuilder_ForwardsSourceAndPrereleaseToDiscovery()
    {
        const string source = "https://example.test/v3/index.json";
        WithDiscoveredStacks(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetupDependencyPlanBuilder builder = new(_bundleReader, _stackCatalog);

        await builder.BuildDependencyPlanAsync(
            Options(["node"], source: source, includePrerelease: true),
            FeaturePlan("node"),
            SetupProfileScope.Unconstrained,
            CancellationToken.None);

        await _stackCatalog.Received(1).GetStacksAsync(source, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FeatureResolver_PromptOffersDiscoveredStacks()
    {
        WithDiscoveredStacks(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["node"] = "azure.functions.cli.workloads.node",
            ["powershell"] = "azure.functions.cli.workloads.powershell",
        });
        IWorkloadStore store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);
        ICliConfigurationProvider configuration = Substitute.For<ICliConfigurationProvider>();
        configuration.GetProjectConfiguration(Arg.Any<DirectoryInfo>())
            .Returns(new ConfigurationBuilder().Build());
        SelectAllInteractionService interaction = new();
        SetupFeatureResolver resolver = new(
            interaction,
            store,
            configuration,
            _stackCatalog);

        SetupFeaturePlan? plan = await resolver.ResolveFeaturesAsync(
            Options([]),
            CancellationToken.None);

        // powershell is absent from the built-in list, so seeing it offered and
        // planned proves the prompt is driven by discovery.
        interaction.MultiSelectionChoices.Should().ContainSingle()
            .Which.Select(choice => choice.Value).Should().Contain(["node", "powershell"]);
        plan.Should().NotBeNull();
        plan!.Features.Should().Contain("powershell");
    }

    private sealed class SelectAllInteractionService : TestInteractionService
    {
        public override bool IsInteractive => true;

        public override Task<IReadOnlyList<string>> PromptForMultiSelectionAsync(
            string title,
            IEnumerable<MultiSelectionChoice> choices,
            CancellationToken cancellationToken = default)
        {
            var list = choices.ToList();
            MultiSelectionChoices.Add(list);
            return Task.FromResult<IReadOnlyList<string>>([.. list.Select(choice => choice.Value)]);
        }
    }

    private sealed class EmptySelectionInteractionService : TestInteractionService
    {
        public override bool IsInteractive => true;
    }

    [Theory]
    [InlineData(true, false, null, "runtime")]
    [InlineData(false, true, null, "runtime")]
    [InlineData(true, false, "powershell", "powershell")]
    [InlineData(false, true, "powershell", "powershell")]
    public async Task FeatureResolver_PromptsSuppressed_UsesProjectDefaultOrRuntime(
        bool assumeYes, bool json, string? configuredRuntime, string expectedFeature)
    {
        WithDiscoveredStacks(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["node"] = "contoso.workloads.node",
            ["powershell"] = "contoso.workloads.powershell",
        });
        IWorkloadStore store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);
        ICliConfigurationProvider configuration = Substitute.For<ICliConfigurationProvider>();
        configuration.GetProjectConfiguration(Arg.Any<DirectoryInfo>()).Returns(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{CliConfigurationNames.StackSectionName}:{CliConfigurationNames.StackRuntimeKey}"] = configuredRuntime,
            }).Build());
        SelectAllInteractionService interaction = new();
        SetupFeatureResolver resolver = new(interaction, store, configuration, _stackCatalog);

        SetupFeaturePlan? plan = await resolver.ResolveFeaturesAsync(
            Options([]) with { AssumeYes = assumeYes, OutputMode = json ? SetupOutputMode.Json : SetupOutputMode.Plain },
            CancellationToken.None);

        plan.Should().NotBeNull();
        plan!.Features.Should().Equal([expectedFeature]);
        interaction.MultiSelectionChoices.Should().BeEmpty();
        interaction.Lines.Should().BeEmpty();
        await store.DidNotReceive().GetWorkloadsAsync(Arg.Any<CancellationToken>());
        await _stackCatalog.Received(configuredRuntime is null ? 0 : 1).GetStacksAsync(
            Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FeatureResolver_EmptySnapshot_FailsInsteadOfReportingAllStacksInstalled()
    {
        WithDiscoveredStacks(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        IWorkloadStore store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);
        ICliConfigurationProvider configuration = Substitute.For<ICliConfigurationProvider>();
        configuration.GetProjectConfiguration(Arg.Any<DirectoryInfo>()).Returns(new ConfigurationBuilder().Build());
        SelectAllInteractionService interaction = new();
        SetupFeatureResolver resolver = new(interaction, store, configuration, _stackCatalog);

        await FluentActions.Awaiting(() => resolver.ResolveFeaturesAsync(Options([]), CancellationToken.None))
            .Should().ThrowAsync<SetupConfigurationException>().WithMessage("*No eligible stacks*");

        interaction.MultiSelectionChoices.Should().BeEmpty();
        interaction.Lines.Should().BeEmpty();
        await store.DidNotReceive().GetWorkloadsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FeatureResolver_AllEligibleStacksInstalled_ReturnsNullWithoutPrompting()
    {
        const string packageId = "contoso.workloads.powershell";
        WithDiscoveredStacks(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["powershell"] = packageId });
        IWorkloadStore store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns([new WorkloadEntry { PackageId = packageId, PackageVersion = "1.0.0" }]);
        ICliConfigurationProvider configuration = Substitute.For<ICliConfigurationProvider>();
        configuration.GetProjectConfiguration(Arg.Any<DirectoryInfo>()).Returns(new ConfigurationBuilder().Build());
        SelectAllInteractionService interaction = new();
        SetupFeatureResolver resolver = new(interaction, store, configuration, _stackCatalog);

        SetupFeaturePlan? plan = await resolver.ResolveFeaturesAsync(Options([]), CancellationToken.None);

        plan.Should().BeNull();
        interaction.MultiSelectionChoices.Should().BeEmpty();
        interaction.Lines.Should().Contain(line => line.Contains("[✓] powershell", StringComparison.Ordinal));
        await store.Received(1).GetWorkloadsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FeatureResolver_EmptySelection_FailsInsteadOfReportingAllStacksInstalled()
    {
        WithDiscoveredStacks(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["powershell"] = "contoso.workloads.powershell",
        });
        IWorkloadStore store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);
        ICliConfigurationProvider configuration = Substitute.For<ICliConfigurationProvider>();
        configuration.GetProjectConfiguration(Arg.Any<DirectoryInfo>()).Returns(new ConfigurationBuilder().Build());
        EmptySelectionInteractionService interaction = new();
        SetupFeatureResolver resolver = new(interaction, store, configuration, _stackCatalog);

        await FluentActions.Awaiting(() => resolver.ResolveFeaturesAsync(Options([]), CancellationToken.None))
            .Should().ThrowAsync<SetupConfigurationException>().WithMessage("At least one setup feature is required.");

        interaction.MultiSelectionChoices.Should().ContainSingle().Which.Select(choice => choice.Value)
            .Should().Equal(["powershell"]);
        interaction.Lines.Should().NotContain(line => line.Contains("Already installed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlanBuilder_AmbiguousAlias_FailsInsteadOfInstallingAnArbitraryPackage()
    {
        _stackCatalog.GetStacksAsync(Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new SetupStackSnapshot(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "node" }));
        SetupDependencyPlanBuilder builder = new(_bundleReader, _stackCatalog);

        SetupDependencyPlan plan = await builder.BuildDependencyPlanAsync(
            Options(["node"]),
            FeaturePlan("node"),
            SetupProfileScope.Unconstrained,
            CancellationToken.None);

        plan.Dependencies.Should().NotContain(d => d.Kind == SetupDependencyKind.Stack);
        plan.Dependencies.Should().NotContain(d => d.Kind == SetupDependencyKind.Worker);
        plan.Failures.Should().ContainSingle()
            .Which.Message.Should().Contain("More than one workload package on this feed claims");
    }

    [Fact]
    public async Task FeatureResolver_SecondaryAlias_FoldsOntoThePrimaryNameEverywhere()
    {
        // Worker ids are built by concatenation and templates are keyed by the
        // primary name, so carrying "nodejs" forward asks for Workers.nodejs and
        // skips templates. WorkerRuntimes has to fold too, since setup.started
        // is emitted from the plan before any dependency is resolved.
        WithSecondaryAlias();

        SetupFeaturePlan? featurePlan = await Resolver().ResolveFeaturesAsync(Options(["nodejs"]), CancellationToken.None);

        featurePlan.Should().NotBeNull();
        featurePlan!.Features.Should().Equal(["node"]);
        featurePlan.WorkerRuntimes.Should().Equal(["node"]);
        featurePlan.RuntimeFeatures.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Name = "node", ProfileRuntime = "node" });

        SetupDependencyPlanBuilder builder = new(_bundleReader, _stackCatalog);
        SetupDependencyPlan plan = await builder.BuildDependencyPlanAsync(
            Options(["nodejs"]),
            featurePlan,
            SetupProfileScope.Unconstrained,
            CancellationToken.None);

        plan.Failures.Should().BeEmpty();
        plan.Dependencies.Should().ContainSingle(d => d.Kind == SetupDependencyKind.Stack)
            .Which.PackageId.Should().Be("contoso.workloads.node");
        plan.Dependencies.Should().ContainSingle(d => d.Kind == SetupDependencyKind.Templates)
            .Which.PackageId.Should().Be("contoso.workloads.templates.node");
        plan.Dependencies.Should().ContainSingle(d => d.Kind == SetupDependencyKind.Worker)
            .Which.PackageId.Should().EndWith("node");
    }

    [Fact]
    public async Task FeatureResolver_SecondaryAlias_IsCheckedAgainstTheProfileByPrimaryName()
    {
        // Profiles list canonical runtimes, so an alternate spelling has to fold
        // before the support check or a legitimate stack reads unsupported.
        WithSecondaryAlias();

        SetupFeaturePlan? featurePlan = await Resolver().ResolveFeaturesAsync(Options(["nodejs"]), CancellationToken.None);
        SetupDependencyPlanBuilder builder = new(_bundleReader, _stackCatalog);

        SetupDependencyPlan plan = await builder.BuildDependencyPlanAsync(
            Options(["nodejs"]),
            featurePlan!,
            ProfileSupporting("node"),
            CancellationToken.None);

        plan.Failures.Should().BeEmpty();
        plan.Dependencies.Should().Contain(d => d.Kind == SetupDependencyKind.Stack);
    }

    [Fact]
    public async Task FeatureResolver_BothSpellingsOfOneStack_CollapseToOne()
    {
        // Dedup runs on the folded name, so --features node,nodejs can't reach
        // the plan as two entries and double every dependency.
        WithSecondaryAlias();

        SetupFeaturePlan? featurePlan = await Resolver().ResolveFeaturesAsync(Options(["node", "nodejs"]), CancellationToken.None);

        featurePlan.Should().NotBeNull();
        featurePlan!.Features.Should().Equal(["node"]);
        featurePlan.RuntimeFeatures.Should().ContainSingle();

        SetupDependencyPlanBuilder builder = new(_bundleReader, _stackCatalog);
        SetupDependencyPlan plan = await builder.BuildDependencyPlanAsync(
            Options(["node", "nodejs"]),
            featurePlan,
            SetupProfileScope.Unconstrained,
            CancellationToken.None);

        plan.Failures.Should().BeEmpty();
        plan.Dependencies.Should().ContainSingle(d => d.Kind == SetupDependencyKind.Stack);
        plan.Dependencies.Should().ContainSingle(d => d.Kind == SetupDependencyKind.Templates);
        plan.Dependencies.Should().ContainSingle(d => d.Kind == SetupDependencyKind.Worker);
    }

    [Fact]
    public async Task FeatureResolver_HostOnly_NeverAsksTheCatalog()
    {
        // Folding must stay lazy; a host-only run has no stack to resolve.
        WithSecondaryAlias();

        await Resolver().ResolveFeaturesAsync(Options(["host"]), CancellationToken.None);

        await _stackCatalog.DidNotReceive().GetStacksAsync(Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    private void WithSecondaryAlias()
        => _stackCatalog.GetStacksAsync(Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new SetupStackSnapshot(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["node"] = "contoso.workloads.node" },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["node"] = "contoso.workloads.templates.node" },
                AmbiguousAliases: null,
                SecondaryAliases: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["nodejs"] = "node" }));

    private SetupFeatureResolver Resolver()
    {
        IWorkloadStore store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);
        ICliConfigurationProvider configuration = Substitute.For<ICliConfigurationProvider>();
        configuration.GetProjectConfiguration(Arg.Any<DirectoryInfo>()).Returns(new ConfigurationBuilder().Build());

        return new SetupFeatureResolver(new TestInteractionService(), store, configuration, _stackCatalog);
    }

    private static SetupProfileScope ProfileSupporting(params string[] runtimes)
        => new(new ResolvedProfile(
            "test",
            new ProfileSourceInfo(ProfileSourceKind.BuiltIn, "built-in"),
            Sku: null,
            ProfileStatus.Stable,
            DeprecationUrl: null,
            VersionRange.All,
            new Dictionary<string, VersionRange>(StringComparer.OrdinalIgnoreCase),
            ExtensionBundleVersionRange: null,
            runtimes,
            Notes: null));

    [Fact]
    public async Task FeatureResolver_DotNetSecondaryAlias_GetsDotNetHandlingNotTheGenericPath()
    {
        // dotnet installs no worker, no bundle, and uses a distinct profile
        // runtime. Folding after the switch would have routed an alternate
        // spelling down the generic arm and picked up all three.
        _stackCatalog.GetStacksAsync(Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new SetupStackSnapshot(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["dotnet"] = "contoso.workloads.dotnet" },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                AmbiguousAliases: null,
                SecondaryAliases: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["csharp"] = "dotnet" }));

        SetupFeaturePlan? aliased = await Resolver().ResolveFeaturesAsync(Options(["csharp"]), CancellationToken.None);
        SetupFeaturePlan? direct = await Resolver().ResolveFeaturesAsync(Options(["dotnet"]), CancellationToken.None);

        aliased.Should().NotBeNull();
        aliased!.Features.Should().Equal(direct!.Features);
        aliased.WorkerRuntimes.Should().Equal(direct.WorkerRuntimes);
        aliased.IncludeExtensionBundle.Should().Be(direct.IncludeExtensionBundle);
        aliased.RuntimeFeatures.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(direct.RuntimeFeatures.Single());
    }

    [Fact]
    public async Task FeatureResolver_AlternateOfAContestedStack_FailsClosed()
    {
        // The end of the path the catalog test guards: an alternate spelling of
        // a contested stack must fold and be refused, not slip through as an
        // unknown runtime and plan a worker for a package that doesn't exist.
        _stackCatalog.GetStacksAsync(Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new SetupStackSnapshot(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "node" },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["nodejs"] = "node" }));

        SetupFeaturePlan? featurePlan = await Resolver().ResolveFeaturesAsync(Options(["nodejs"]), CancellationToken.None);
        featurePlan.Should().NotBeNull();
        featurePlan!.Features.Should().Equal(["node"], "the alternate has to fold before anything is recorded");

        SetupDependencyPlanBuilder builder = new(_bundleReader, _stackCatalog);
        SetupDependencyPlan plan = await builder.BuildDependencyPlanAsync(
            Options(["nodejs"]),
            featurePlan,
            SetupProfileScope.Unconstrained,
            CancellationToken.None);

        plan.Failures.Should().ContainSingle()
            .Which.Message.Should().Contain("More than one workload package on this feed claims");
        plan.Dependencies.Should().NotContain(d => d.Kind == SetupDependencyKind.Worker);
        plan.Dependencies.Should().NotContain(d => d.Kind == SetupDependencyKind.Stack);
    }

    [Fact]
    public async Task FeatureResolver_PromptSkipsStacksNamedAfterBuiltInFeatures()
    {
        // Selecting one would come back as the feature word, dispatch to the
        // built-in arm, and quietly install host plus bundle instead of the
        // package that was shown.
        WithDiscoveredStacks(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["node"] = "azure.functions.cli.workloads.node",
            ["runtime"] = "contoso.workloads.runtime",
            ["host"] = "contoso.workloads.host",
        });
        IWorkloadStore store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);
        ICliConfigurationProvider configuration = Substitute.For<ICliConfigurationProvider>();
        configuration.GetProjectConfiguration(Arg.Any<DirectoryInfo>()).Returns(new ConfigurationBuilder().Build());
        SelectAllInteractionService interaction = new();
        SetupFeatureResolver resolver = new(interaction, store, configuration, _stackCatalog);

        await resolver.ResolveFeaturesAsync(Options([]), CancellationToken.None);

        IEnumerable<string> offered = interaction.MultiSelectionChoices.Should().ContainSingle()
            .Which.Select(choice => choice.Value);
        offered.Should().Contain("node");
        offered.Should().NotContain(["runtime", "host"]);
    }

    [Theory]
    [InlineData("host", false)]
    [InlineData("runtime", false)]
    [InlineData(".net", false)]
    [InlineData("dotnet-inprocess", false)]
    [InlineData("dotnet-isolated", false)]
    [InlineData("dotnet", true)]
    public async Task FeatureResolver_StackNamedAfterAFeatureWord_HonorsItsOfferContract(string name, bool shouldOffer)
    {
        // These expected CLI semantics are independent of the production keyword
        // collection so omitting a reserved name cannot also remove its test case.
        const string packageId = "contoso.workloads.thing";
        WithDiscoveredStacks(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [name] = packageId });
        IWorkloadStore store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);
        ICliConfigurationProvider configuration = Substitute.For<ICliConfigurationProvider>();
        configuration.GetProjectConfiguration(Arg.Any<DirectoryInfo>()).Returns(new ConfigurationBuilder().Build());
        SelectAllInteractionService interaction = new();
        SetupFeatureResolver resolver = new(interaction, store, configuration, _stackCatalog);

        if (!shouldOffer)
        {
            await FluentActions.Awaiting(() => resolver.ResolveFeaturesAsync(Options([]), CancellationToken.None))
                .Should().ThrowAsync<SetupConfigurationException>().WithMessage("*No eligible stacks*");

            interaction.MultiSelectionChoices.Should().BeEmpty();
            interaction.Lines.Should().BeEmpty();
            await store.DidNotReceive().GetWorkloadsAsync(Arg.Any<CancellationToken>());
            return;
        }

        SetupFeaturePlan? featurePlan = await resolver.ResolveFeaturesAsync(Options([]), CancellationToken.None);

        interaction.MultiSelectionChoices.Should().ContainSingle().Which.Select(choice => choice.Value)
            .Should().Equal([name]);
        featurePlan.Should().NotBeNull();
        featurePlan!.Features.Should().Equal([name]);
        SetupDependencyPlanBuilder builder = new(_bundleReader, _stackCatalog);
        SetupDependencyPlan plan = await builder.BuildDependencyPlanAsync(
            Options([]),
            featurePlan!,
            SetupProfileScope.Unconstrained,
            CancellationToken.None);

        plan.Dependencies.Should().Contain(
            d => d.Kind == SetupDependencyKind.Stack && d.PackageId == packageId,
            $"'{name}' was offered as a stack, so selecting it must plan {packageId}");
    }

    private void WithDiscoveredStacks(
        Dictionary<string, string> stacks,
        Dictionary<string, string>? templates = null)
    {
        _stackCatalog.GetStacksAsync(Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new SetupStackSnapshot(
                stacks,
                templates ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
    }

    private static CatalogSearchPage Page(params CatalogSearchResult[] items) => new(items, items.Length == 0 ? 0 : 100);

    private static SetupFeaturePlan FeaturePlan(string runtime)
        => new(
            [runtime],
            [new SetupRuntimeFeature(runtime, runtime, InstallWorker: true)],
            [runtime],
            IncludeExtensionBundle: false);

    private static SetupCommandOptions Options(
        IReadOnlyList<string> features,
        string? source = null,
        bool includePrerelease = false)
        => new(
            new DirectoryInfo(Path.GetTempPath()),
            features,
            [],
            source,
            SetupInstallPolicy.LatestCompatible,
            includePrerelease,
            NonInteractive: false,
            AssumeYes: false,
            Check: true,
            SetupOutputMode.Plain);
}
