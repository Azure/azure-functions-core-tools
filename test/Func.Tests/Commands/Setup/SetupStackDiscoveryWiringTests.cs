// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Configuration;
using NSubstitute;

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
