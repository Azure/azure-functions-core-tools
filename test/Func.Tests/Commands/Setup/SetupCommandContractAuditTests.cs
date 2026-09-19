// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting.FirstRun;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

// Pins the desired contracts against the f99f1b80 audit baseline without changing production code.
public sealed class SetupCommandContractAuditTests
{
    private const string JavaPackage = "contoso.workloads.java";
    private const string NodePackage = "contoso.workloads.node";

    [Theory]
    [InlineData("dotnet")]
    [InlineData("dotnet-isolated")]
    public async Task ResolveFeaturesAsync_DotNetSpellingRedirectedToJava_ThrowsConfigurationException(string spelling)
    {
        AuditHarness harness = CreateHarness(DotNetRedirectSnapshot());

        await FluentActions.Awaiting(() => harness.FeatureResolver.ResolveFeaturesAsync(Options([spelling]), CancellationToken.None))
            .Should().ThrowAsync<SetupConfigurationException>();

        harness.Interaction.PromptCount.Should().Be(0);
    }

    [Theory]
    [InlineData("dotnet")]
    [InlineData("dotnet-isolated")]
    public async Task RunAsync_DotNetSpellingRedirectedToJava_FailsBeforeInstallingOrMarkingComplete(string spelling)
    {
        AuditHarness harness = CreateHarness(DotNetRedirectSnapshot());

        SetupRunResult result = await harness.Runner.RunAsync(Options([spelling], json: true), CancellationToken.None);

        result.ExitCode.Should().Be(1);
        JsonElement[] events = ReadEvents(harness.Interaction.Lines);
        events.Should().ContainSingle().Which.GetProperty("type").GetString().Should().Be("setup.failed");
        await harness.Installer.DidNotReceive().EnsureDependencyAsync(
            Arg.Any<SetupCommandOptions>(), Arg.Any<SetupDependency>(), Arg.Any<CancellationToken>());
        await harness.FirstRunStore.DidNotReceive().MarkCompleteAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("dotnet")]
    [InlineData("dotnet-isolated")]
    public async Task ResolveFeaturesAsync_UnredirectedDotNetSpelling_PreservesDotNetSemantics(string spelling)
    {
        AuditHarness harness = CreateHarness(SetupDependency.BuiltInStackSnapshot);

        SetupFeaturePlan? plan = await harness.FeatureResolver.ResolveFeaturesAsync(Options([spelling]), CancellationToken.None);

        plan.Should().NotBeNull();
        plan!.Features.Should().Equal("dotnet");
        plan.RuntimeFeatures.Should().ContainSingle().Which.Should()
            .BeEquivalentTo(new SetupRuntimeFeature("dotnet", "dotnet-isolated", InstallWorker: false));
        plan.WorkerRuntimes.Should().BeEmpty();
        plan.IncludeExtensionBundle.Should().BeFalse();
        harness.Interaction.PromptCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveFeaturesAsync_UnusableSnapshotWithEmptyStore_ThrowsInsteadOfReturningNull(bool allAmbiguous)
    {
        AuditHarness harness = CreateHarness(UnusableSnapshot(allAmbiguous));

        await FluentActions.Awaiting(() => harness.FeatureResolver.ResolveFeaturesAsync(Options(), CancellationToken.None))
            .Should().ThrowAsync<SetupConfigurationException>();

        harness.Interaction.PromptCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_UnusableSnapshotWithEmptyStore_FailsWithoutMarkingComplete(bool allAmbiguous)
    {
        AuditHarness harness = CreateHarness(UnusableSnapshot(allAmbiguous));

        SetupRunResult result = await harness.Runner.RunAsync(Options(), CancellationToken.None);

        result.ExitCode.Should().Be(1);
        harness.Interaction.PromptCount.Should().Be(0);
        harness.Interaction.Lines.Should().Contain(line => line.StartsWith("ERROR:", StringComparison.Ordinal));
        await harness.Profiles.DidNotReceive().ResolveProfileScopesAsync(
            Arg.Any<SetupCommandOptions>(), Arg.Any<SetupRenderer>(), Arg.Any<CancellationToken>());
        await harness.Installer.DidNotReceive().EnsureDependencyAsync(
            Arg.Any<SetupCommandOptions>(), Arg.Any<SetupDependency>(), Arg.Any<CancellationToken>());
        await harness.FirstRunStore.DidNotReceive().MarkCompleteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveFeaturesAsync_AllLegitimateStacksInstalled_ReturnsNullWithoutPrompting()
    {
        SetupStackSnapshot snapshot = NormalSnapshot();
        AuditHarness harness = CreateHarness(snapshot, InstalledStacks(snapshot));

        SetupFeaturePlan? plan = await harness.FeatureResolver.ResolveFeaturesAsync(Options(), CancellationToken.None);

        plan.Should().BeNull();
        harness.Interaction.PromptCount.Should().Be(0);
        harness.Interaction.MultiSelectionChoices.Should().BeEmpty();
        harness.Interaction.AllOutput.Should().Contain("node").And.Contain("java");
    }

    private static AuditHarness CreateHarness(
        SetupStackSnapshot snapshot,
        IReadOnlyList<WorkloadEntry>? installed = null)
    {
        AuditInteractionService interaction = new();
        var catalog = Substitute.For<ISetupStackCatalog>();
        catalog.GetStacksAsync(Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(snapshot);
        var store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(installed ?? []);
        var configuration = Substitute.For<ICliConfigurationProvider>();
        configuration.GetProjectConfiguration(Arg.Any<DirectoryInfo>()).Returns(new ConfigurationBuilder().Build());
        SetupFeatureResolver features = new(interaction, store, configuration, catalog);
        var profiles = Substitute.For<ISetupProfileScopeResolver>();
        profiles.ResolveProfileScopesAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupRenderer>(), Arg.Any<CancellationToken>())
            .Returns([SetupProfileScope.Unconstrained]);
        var bundleReader = Substitute.For<IHostJsonBundleSectionReader>();
        bundleReader.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>()).Returns((HostJsonBundleSection?)null);
        SetupDependencyPlanBuilder planner = new(bundleReader, catalog);
        var installer = Substitute.For<ISetupDependencyInstaller>();
        installer.EnsureDependencyAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupDependency>(), Arg.Any<CancellationToken>())
            .Returns(call => Satisfied(call.Arg<SetupDependency>()));
        var firstRunStore = Substitute.For<IFirstRunStateStore>();
        SetupRunner runner = new(interaction, features, profiles, planner, installer, firstRunStore);
        return new AuditHarness(features, runner, profiles, installer, firstRunStore, interaction);
    }

    private static SetupStackSnapshot DotNetRedirectSnapshot()
        => new(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["java"] = JavaPackage },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            SecondaryAliases: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["dotnet"] = "java" });

    private static SetupStackSnapshot NormalSnapshot()
        => new(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["node"] = NodePackage, ["java"] = JavaPackage },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    private static SetupStackSnapshot UnusableSnapshot(bool allAmbiguous)
    {
        if (allAmbiguous)
        {
            SetupStackSnapshot builtIn = SetupDependency.BuiltInStackSnapshot;
            return builtIn with
            {
                AmbiguousAliases = new HashSet<string>(builtIn.StackPackageIds.Keys, StringComparer.OrdinalIgnoreCase),
            };
        }

        return new SetupStackSnapshot(
            SetupFeatureResolver.ResolverKeywords.ToDictionary(name => name, name => $"contoso.reserved.{name}", StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static WorkloadEntry[] InstalledStacks(SetupStackSnapshot snapshot)
        => [.. snapshot.StackPackageIds.Values.Select(InstalledStack)];

    private static WorkloadEntry InstalledStack(string packageId)
        => new() { PackageId = packageId, PackageVersion = "1.0.0", Kind = WorkloadKind.Workload };

    private static SetupDependencyResult Satisfied(SetupDependency dependency)
        => SetupDependencyResult.Satisfied(dependency, dependency.PackageId, "1.0.0", "Already installed.");

    private static SetupCommandOptions Options(
        IReadOnlyList<string>? features = null,
        bool json = false)
        => new(
            new DirectoryInfo("setup-contract-audit"),
            features ?? [],
            [],
            Source: null,
            SetupInstallPolicy.IfNeeded,
            IncludePrerelease: false,
            NonInteractive: false,
            AssumeYes: false,
            Check: false,
            OutputMode: json ? SetupOutputMode.Json : SetupOutputMode.Plain);

    private static JsonElement[] ReadEvents(IReadOnlyList<string> lines)
    {
        lines.Should().NotBeEmpty();
        List<JsonElement> events = [];
        foreach (string line in lines)
        {
            using var document = JsonDocument.Parse(line);
            document.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
            document.RootElement.GetProperty("type").GetString().Should().NotBeNullOrEmpty();
            events.Add(document.RootElement.Clone());
        }

        return [.. events];
    }

    private sealed record AuditHarness(
        SetupFeatureResolver FeatureResolver,
        SetupRunner Runner,
        ISetupProfileScopeResolver Profiles,
        ISetupDependencyInstaller Installer,
        IFirstRunStateStore FirstRunStore,
        AuditInteractionService Interaction);

    private sealed class AuditInteractionService : TestInteractionService
    {
        public override bool IsInteractive => true;

        public int PromptCount { get; private set; }

        public override async Task<IReadOnlyList<string>> PromptForMultiSelectionAsync(
            string title, IEnumerable<string> choices, CancellationToken cancellationToken = default)
        {
            List<string> offered = [.. choices];
            PromptCount++;
            await base.PromptForMultiSelectionAsync(title, offered, cancellationToken);
            return offered;
        }

        public override async Task<IReadOnlyList<string>> PromptForMultiSelectionAsync(
            string title, IEnumerable<MultiSelectionChoice> choices, CancellationToken cancellationToken = default)
        {
            List<MultiSelectionChoice> offered = [.. choices];
            PromptCount++;
            await base.PromptForMultiSelectionAsync(title, offered, cancellationToken);
            return [.. offered.Select(choice => choice.Value)];
        }
    }
}