// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public class SetupPlanningCompletenessTests
{
    public static IEnumerable<object[]> MissingBuiltInStacks()
        => SetupDependency.BuiltInStackSnapshot.StackNames.Concat([SetupRuntimes.DotNetProfileRuntime])
            .SelectMany(name => new[] { false, true }.Select(check => new object[] { name, check }));

    [Theory]
    [MemberData(nameof(MissingBuiltInStacks))]
    public async Task RunAsync_RequestedBuiltInStackMissingFromDiscoveredFeed_ReportsFailure(string requested, bool check)
    {
        var workloadCatalog = Substitute.For<IWorkloadCatalog>();
        workloadCatalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(new CatalogSearchPage([
                new CatalogSearchResult("contoso.java", new NuGet.Versioning.NuGetVersion("1.0.0"), null, null,
                    ["java"], new NuGet.Configuration.PackageSource("https://java-only.test/v3/index.json")) { Kind = "workload" },
            ], 1, TotalHits: 1));
        var catalog = new SetupStackCatalog(workloadCatalog);
        var interaction = new TestInteractionService();
        var features = new SetupFeatureResolver(interaction, Substitute.For<IWorkloadStore>(), Substitute.For<ICliConfigurationProvider>(), catalog);
        var planner = new SetupDependencyPlanBuilder(Substitute.For<IHostJsonBundleSectionReader>(), catalog);
        var profiles = Substitute.For<ISetupProfileScopeResolver>();
        profiles.ResolveProfileScopesAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupRenderer>(), Arg.Any<CancellationToken>())
            .Returns([SetupProfileScope.Unconstrained]);
        var installer = Substitute.For<ISetupDependencyInstaller>();
        installer.EnsureDependencyAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupDependency>(), Arg.Any<CancellationToken>())
            .Returns(call => SetupDependencyResult.Satisfied(call.Arg<SetupDependency>(), call.Arg<SetupDependency>().PackageId, "1.0.0", "installed"));
        var runner = new SetupRunner(interaction, features, profiles, planner, installer);
        SetupCommandOptions options = new(new DirectoryInfo(Path.GetTempPath()), [requested], [], "https://java-only.test/v3/index.json",
            SetupInstallPolicy.IfNeeded, false, NonInteractive: true, AssumeYes: true, check, SetupOutputMode.Json);

        SetupRunResult result = await runner.RunAsync(options, CancellationToken.None);

        result.ExitCode.Should().Be(1);
        interaction.AllOutput.Should().Contain("setup.failed").And.Contain("not available from the selected workload catalog").And.Contain("--source");
        string canonical = requested == SetupRuntimes.DotNetProfileRuntime ? SetupRuntimes.DotNetFeature : requested;
        interaction.AllOutput.Should().Contain(canonical);
        await installer.DidNotReceive().EnsureDependencyAsync(Arg.Any<SetupCommandOptions>(),
            Arg.Is<SetupDependency>(dependency => dependency.Kind == SetupDependencyKind.Worker
                || dependency.Kind == SetupDependencyKind.Stack || dependency.Kind == SetupDependencyKind.Templates),
            Arg.Any<CancellationToken>());
        if (!check)
        {
            await installer.DidNotReceiveWithAnyArgs().EnsureDependencyAsync(default!, default!, default);
        }
        else
        {
            await installer.Received(1).EnsureDependencyAsync(Arg.Is<SetupCommandOptions>(o => o.Check),
                Arg.Is<SetupDependency>(dependency => dependency.Kind == SetupDependencyKind.Host), Arg.Any<CancellationToken>());
        }

        await workloadCatalog.Received(1).SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("dotnet")]
    [InlineData("node")]
    public async Task PlanBuilder_OfflineBuiltInSnapshot_StillPlansRequiredStack(string stack)
    {
        var catalog = Substitute.For<ISetupStackCatalog>();
        catalog.GetStacksAsync(Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(SetupDependency.BuiltInStackSnapshot);
        var planner = new SetupDependencyPlanBuilder(Substitute.For<IHostJsonBundleSectionReader>(), catalog);
        var features = new SetupFeatureResolver(new TestInteractionService(), Substitute.For<IWorkloadStore>(),
            Substitute.For<ICliConfigurationProvider>(), catalog);
        SetupCommandOptions options = new(new DirectoryInfo(Path.GetTempPath()), [stack], [], null,
            SetupInstallPolicy.IfNeeded, false, NonInteractive: true, AssumeYes: true, Check: true, SetupOutputMode.Plain);
        SetupFeaturePlan? featurePlan = await features.ResolveFeaturesAsync(options, CancellationToken.None);

        SetupDependencyPlan plan = await planner.BuildDependencyPlanAsync(options, featurePlan!, SetupProfileScope.Unconstrained, CancellationToken.None);

        plan.Failures.Should().BeEmpty();
        plan.Dependencies.Should().ContainSingle(dependency => dependency.Kind == SetupDependencyKind.Stack)
            .Which.PackageId.Should().Be(SetupDependency.BuiltInStackSnapshot.StackPackageId(stack));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discovery_ConvertedFailure_PreservesOriginalException(bool malformed)
    {
        Exception original = malformed ? new InvalidDataException("bad page")
            : new InvalidWorkloadSourceException("bad source", new ArgumentException("bad URL"));
        var catalog = Substitute.For<IWorkloadCatalog>();
        catalog.SearchPageAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns<CatalogSearchPage>(_ => throw original);

        var error = await FluentActions.Awaiting(() => new SetupStackCatalog(catalog).GetStacksAsync(null, false, CancellationToken.None))
            .Should().ThrowAsync<SetupConfigurationException>();

        error.Which.InnerException.Should().BeSameAs(original);
    }

    [Fact]
    public async Task Installer_ConvertedSourceFailure_PreservesOriginalException()
    {
        var original = new InvalidWorkloadSourceException("bad source", new ArgumentException("bad URL"));
        var catalog = Substitute.For<IWorkloadCatalog>();
        catalog.ResolveLatestVersionAsync(Arg.Any<string>(), Arg.Any<bool?>(), Arg.Any<NuGet.Versioning.NuGetVersion?>(),
                Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<ResolvedPackage?>(_ => throw original);
        var store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);
        var installer = new SetupDependencyInstaller(new TestInteractionService(), store, catalog, Substitute.For<IWorkloadInstaller>());
        SetupCommandOptions options = new(new DirectoryInfo(Path.GetTempPath()), ["host"], [], null,
            SetupInstallPolicy.LatestCompatible, false, NonInteractive: true, AssumeYes: true, Check: true, SetupOutputMode.Plain);

        var error = await FluentActions.Awaiting(() => installer.EnsureDependencyAsync(options, SetupDependency.Host(null), CancellationToken.None))
            .Should().ThrowAsync<SetupConfigurationException>();

        error.Which.InnerException.Should().BeSameAs(original);
    }
}