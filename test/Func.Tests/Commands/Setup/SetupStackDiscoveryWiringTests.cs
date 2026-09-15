// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Profiles;
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

    [Fact]
    public async Task PlanBuilder_UsesDiscoveredPackageId_NotTheBuiltInOne()
    {
        // A stack the built-in list doesn't know about must still be planned,
        // with the package id the catalog reported.
        const string discoveredId = "contoso.functions.cli.workloads.java";
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
    public async Task PlanBuilder_JavaIsPlanned_EvenThoughBuiltInListOmitsIt()
    {
        SetupDependency.BuiltInStackSnapshot.SupportsStack("java").Should().BeFalse();
        WithDiscoveredStacks(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["java"] = "azure.functions.cli.workloads.java",
        });
        SetupDependencyPlanBuilder builder = new(_bundleReader, _stackCatalog);

        SetupDependencyPlan plan = await builder.BuildDependencyPlanAsync(
            Options(["java"]),
            FeaturePlan("java"),
            SetupProfileScope.Unconstrained,
            CancellationToken.None);

        plan.Dependencies.Should().Contain(d => d.Kind == SetupDependencyKind.Stack && d.Name == "java");
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

    /// <summary>
    /// Every name the resolver reserves, plus <c>dotnet</c>, which reaches the
    /// same built-in arm but is deliberately still offerable. Driven off the
    /// production array so the two can't drift.
    /// </summary>
    public static TheoryData<string> FeatureWords
    {
        get
        {
            TheoryData<string> data = [];
            foreach (string keyword in SetupFeatureResolver.ResolverKeywords)
            {
                data.Add(keyword);
            }

            data.Add(SetupRuntimes.DotNetFeature);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(FeatureWords))]
    public async Task FeatureResolver_StackNamedAfterAFeatureWord_IsEitherWithheldOrActuallyPlanned(string name)
    {
        // Every name the switch dispatches on has to land on one side of this:
        // withheld from the prompt, or offered and genuinely planned. Showing a
        // package and then installing something else is the failure. Asserting
        // the invariant rather than a keyword list maintained by hand, since
        // that list is what went wrong.
        const string packageId = "contoso.workloads.thing";
        WithDiscoveredStacks(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [name] = packageId });
        IWorkloadStore store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);
        ICliConfigurationProvider configuration = Substitute.For<ICliConfigurationProvider>();
        configuration.GetProjectConfiguration(Arg.Any<DirectoryInfo>()).Returns(new ConfigurationBuilder().Build());
        SelectAllInteractionService interaction = new();
        SetupFeatureResolver resolver = new(interaction, store, configuration, _stackCatalog);

        SetupFeaturePlan? featurePlan = await resolver.ResolveFeaturesAsync(Options([]), CancellationToken.None);

        bool offered = interaction.MultiSelectionChoices
            .SelectMany(static choices => choices)
            .Any(choice => string.Equals(choice.Value, name, StringComparison.OrdinalIgnoreCase));
        if (!offered)
        {
            return;
        }

        featurePlan.Should().NotBeNull();
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
            AssumeYes: true,
            Check: true,
            SetupOutputMode.Plain);
}
