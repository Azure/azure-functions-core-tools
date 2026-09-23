// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using AwesomeAssertions.Execution;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting.FirstRun;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Spectre.Console;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public sealed class SetupAutomationContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_AlreadyCanceled_DoesNotBeginSetup(bool json)
    {
        using CancellationTokenSource cancellation = new();
        Harness harness = CreateHarness();
        cancellation.Cancel();

        var error = await FluentActions.Awaiting(() => harness.Runner.RunAsync(
            Options(json: json) with { IncludePrerelease = true }, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        error.Which.CancellationToken.Should().Be(cancellation.Token);
        harness.Interaction.Lines.Should().BeEmpty();
        harness.Configuration.ReceivedCalls().Should().BeEmpty();
        harness.Store.ReceivedCalls().Should().BeEmpty();
        harness.Profiles.ReceivedCalls().Should().BeEmpty();
        harness.Installer.ReceivedCalls().Should().BeEmpty();
        harness.FirstRunStore.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(false, false, false, false)]
    public async Task ResolveFeaturesAsync_PromptsUnavailable_DefaultsToRuntime(
        bool assumeYes, bool json, bool nonInteractive, bool interactive)
    {
        Harness harness = CreateHarness(interactive: interactive);

        SetupFeaturePlan? plan = await harness.Features.ResolveFeaturesAsync(
            Options(assumeYes: assumeYes, json: json, nonInteractive: nonInteractive), CancellationToken.None);

        plan.Should().NotBeNull();
        plan!.Features.Should().Equal("runtime");
        plan.RuntimeFeatures.Should().BeEmpty();
        plan.WorkerRuntimes.Should().BeEmpty();
        plan.IncludeExtensionBundle.Should().BeTrue();
        harness.Interaction.PromptCount.Should().Be(0);
        if (assumeYes && !json)
        {
            harness.Interaction.Lines.Should().ContainSingle().Which.Should().StartWith("HINT: No language stack was selected.");
        }
        else
        {
            harness.Interaction.Lines.Should().BeEmpty();
        }

        await harness.Store.DidNotReceive().GetWorkloadsAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, false, false)]
    public async Task ResolveFeaturesAsync_ConfiguredProjectRuntime_TakesPrecedenceOverRuntimeDefault(
        bool assumeYes, bool json, bool nonInteractive)
    {
        Harness harness = CreateHarness(projectRuntime: " dotnet-isolated ");

        SetupFeaturePlan? plan = await harness.Features.ResolveFeaturesAsync(
            Options(assumeYes: assumeYes, json: json, nonInteractive: nonInteractive), CancellationToken.None);

        plan.Should().NotBeNull();
        plan!.Features.Should().Equal("dotnet");
        plan.RuntimeFeatures.Should().ContainSingle().Which.Should()
            .BeEquivalentTo(new SetupRuntimeFeature("dotnet", "dotnet-isolated", InstallWorker: false));
        plan.WorkerRuntimes.Should().BeEmpty();
        plan.IncludeExtensionBundle.Should().BeFalse();
        harness.Interaction.PromptCount.Should().Be(0);
        harness.Interaction.Lines.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task ResolveFeaturesAsync_ExplicitFeatures_KeepTheirMeaningDespiteAutomationAndProjectRuntime(
        bool assumeYes, bool json, bool nonInteractive)
    {
        Harness harness = CreateHarness(projectRuntime: "python");

        SetupFeaturePlan? plan = await harness.Features.ResolveFeaturesAsync(
            Options(["host", "dotnet-isolated", "dotnet"], assumeYes: assumeYes, json: json, nonInteractive: nonInteractive),
            CancellationToken.None);

        plan.Should().NotBeNull();
        plan!.Features.Should().Equal("host", "dotnet");
        plan.RuntimeFeatures.Should().ContainSingle().Which.Should()
            .BeEquivalentTo(new SetupRuntimeFeature("dotnet", "dotnet-isolated", InstallWorker: false));
        plan.WorkerRuntimes.Should().BeEmpty();
        plan.IncludeExtensionBundle.Should().BeFalse();
        harness.Interaction.PromptCount.Should().Be(0);
        harness.Configuration.DidNotReceive().GetProjectConfiguration(Arg.Any<DirectoryInfo>());
    }

    [Fact]
    public async Task ResolveFeaturesAsync_NormalInteractiveRun_OffersBuiltInStacksAndUsesSelection()
    {
        Harness harness = CreateHarness();

        SetupFeaturePlan? plan = await harness.Features.ResolveFeaturesAsync(Options(), CancellationToken.None);

        harness.Interaction.PromptCount.Should().Be(1);
        harness.Interaction.MultiSelectionChoices.Should().ContainSingle().Which.Select(choice => choice.Value)
            .Should().Equal("dotnet", "go", "node", "python");
        plan.Should().NotBeNull();
        plan!.Features.Should().Equal("node");
        plan.WorkerRuntimes.Should().Equal("node");
        plan.IncludeExtensionBundle.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_InteractiveJsonWithoutFeatures_UsesRuntimeEvenWhenAllStacksAreInstalled(bool allStacksInstalled)
    {
        Harness harness = CreateHarness(allStacksInstalled: allStacksInstalled);

        SetupRunResult result = await harness.Runner.RunAsync(Options(json: true), CancellationToken.None);

        result.ExitCode.Should().Be(0);
        harness.Interaction.PromptCount.Should().Be(0);
        JsonElement[] events = ReadEvents(harness.Interaction.Lines);
        JsonElement started = events.Should().ContainSingle(item => EventType(item) == "setup.started").Which;
        started.GetProperty("features").EnumerateArray().Select(item => item.GetString()).Should().Equal("runtime");
        started.GetProperty("worker_runtimes").EnumerateArray().Should().BeEmpty();
        events.Where(item => EventType(item) == "dependency.detected")
            .Select(item => item.GetProperty("dependency_type").GetString()).Should().Equal("host", "extension-bundle");
        events.Should().ContainSingle(item => EventType(item) == "setup.completed")
            .Which.GetProperty("success").GetBoolean().Should().BeTrue();
        events.Should().NotContain(item => EventType(item) == "setup.skipped" || EventType(item) == "setup.failed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_SatisfiedHost_OnlyInstallModeMarksFirstRunComplete(bool check)
    {
        Harness harness = CreateHarness();

        SetupRunResult result = await harness.Runner.RunAsync(Options(["host"], check: check, json: true), CancellationToken.None);

        result.ExitCode.Should().Be(0);
        JsonElement[] events = ReadEvents(harness.Interaction.Lines);
        events.Should().ContainSingle(item => EventType(item) == "dependency.result")
            .Which.GetProperty("status").GetString().Should().Be("satisfied");
        events.Should().ContainSingle(item => EventType(item) == "setup.completed")
            .Which.GetProperty("success").GetBoolean().Should().BeTrue();
        await harness.Installer.Received(1).EnsureDependencyAsync(
            Arg.Any<SetupCommandOptions>(), Arg.Any<SetupDependency>(), Arg.Any<CancellationToken>());
        await harness.FirstRunStore.Received(check ? 0 : 1).MarkCompleteAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_AllStacksInstalledPickerNoOp_OnlyInstallModeMarksFirstRunComplete(bool check)
    {
        Harness harness = CreateHarness(allStacksInstalled: true);

        SetupRunResult result = await harness.Runner.RunAsync(Options(check: check), CancellationToken.None);

        result.ExitCode.Should().Be(0);
        harness.Interaction.PromptCount.Should().Be(0);
        harness.Interaction.MultiSelectionChoices.Should().BeEmpty();
        harness.Interaction.Lines.Should().Contain(line => line.StartsWith("HINT:", StringComparison.Ordinal)
            && line.Contains("Nothing to install", StringComparison.Ordinal));
        await harness.Profiles.DidNotReceive().ResolveProfileScopesAsync(
            Arg.Any<SetupCommandOptions>(), Arg.Any<SetupRenderer>(), Arg.Any<CancellationToken>());
        await harness.Installer.DidNotReceive().EnsureDependencyAsync(
            Arg.Any<SetupCommandOptions>(), Arg.Any<SetupDependency>(), Arg.Any<CancellationToken>());
        await harness.FirstRunStore.Received(check ? 0 : 1).MarkCompleteAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_FailedDependency_DoesNotReportOrPersistCompletion(bool check)
    {
        Harness harness = CreateHarness(dependencyResult: dependency => SetupDependencyResult.Failed(dependency, "Host missing."));

        SetupRunResult result = await harness.Runner.RunAsync(Options(["host"], check: check, json: true), CancellationToken.None);

        result.ExitCode.Should().Be(1);
        JsonElement[] events = ReadEvents(harness.Interaction.Lines);
        events.Should().ContainSingle(item => EventType(item) == "dependency.result")
            .Which.GetProperty("status").GetString().Should().Be("failed");
        events.Should().ContainSingle(item => EventType(item) == "setup.failed");
        events.Should().NotContain(item => EventType(item) == "setup.completed");
        await harness.FirstRunStore.DidNotReceive().MarkCompleteAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_CanceledAsFinalDependencyReturns_DoesNotReportOrPersistCompletion(bool check)
    {
        using CancellationTokenSource cancellation = new();
        Harness harness = CreateHarness(dependencyResult: dependency =>
        {
            if (dependency.Kind == SetupDependencyKind.ExtensionBundle)
            {
                cancellation.Cancel();
            }

            return Satisfied(dependency);
        });

        Exception? error = await Record.ExceptionAsync(() => harness.Runner.RunAsync(
            Options(["runtime"], check: check, json: true), cancellation.Token));

        using AssertionScope scope = new();
        cancellation.IsCancellationRequested.Should().BeTrue();
        error.Should().BeAssignableTo<OperationCanceledException>();
        ReadEvents(harness.Interaction.Lines).Should().NotContain(item => EventType(item) == "setup.completed");
        harness.FirstRunStore.ReceivedCalls().Should()
            .NotContain(call => call.GetMethodInfo().Name == nameof(IFirstRunStateStore.MarkCompleteAsync));
        await harness.Installer.Received(2).EnsureDependencyAsync(
            Arg.Any<SetupCommandOptions>(), Arg.Any<SetupDependency>(), cancellation.Token);
    }

    [Fact]
    public void Renderer_JsonAtNarrowSpectreWidth_PreservesOnePhysicalLineAndEscapedPayload()
    {
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        IAnsiConsole output = CreateConsole(stdout);
        IAnsiConsole error = CreateConsole(stderr);
        SpectreInteractionService interaction = new(new DefaultTheme(), output, error);
        SetupRenderer renderer = new(interaction, SetupOutputMode.Json);
        string message = $"[red]{new string('x', 200)}[/] \"quoted\"\r\nnext\tline \u001b[31m";

        renderer.Warning(message);

        using StringReader reader = new(stdout.ToString());
        string? line = reader.ReadLine();
        line.Should().NotBeNull();
        line!.Length.Should().BeGreaterThan(80);
        reader.ReadLine().Should().BeNull();
        stdout.ToString().Should().Be(line + Environment.NewLine);
        using var document = JsonDocument.Parse(line);
        document.RootElement.GetProperty("type").GetString().Should().Be("setup.warning");
        document.RootElement.GetProperty("message").GetString().Should().Be(message);
        document.RootElement.GetProperty("timestamp").GetDateTimeOffset().Should().NotBe(default);
        stderr.ToString().Should().BeEmpty();
    }

    private static IAnsiConsole CreateConsole(StringWriter writer)
    {
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Interactive = InteractionSupport.No,
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
        });
        console.Profile.Width = 80;
        return console;
    }

    private static Harness CreateHarness(
        bool interactive = true,
        string? projectRuntime = null,
        bool allStacksInstalled = false,
        Func<SetupDependency, SetupDependencyResult>? dependencyResult = null)
    {
        SelectionInteraction interaction = new(interactive);
        var store = Substitute.For<IWorkloadStore>();
        string[] stacks = ["dotnet", "go", "node", "python"];
        IReadOnlyList<WorkloadEntry> installed = allStacksInstalled
            ? [.. stacks.Select(stack => new WorkloadEntry
            {
                PackageId = SetupDependency.Stack(stack).PackageId,
                PackageVersion = "1.0.0",
                Kind = WorkloadKind.Workload,
            })]
            : [];
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(installed);
        var configuration = Substitute.For<ICliConfigurationProvider>();
        configuration.GetProjectConfiguration(Arg.Any<DirectoryInfo>()).Returns(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{CliConfigurationNames.StackSectionName}:{CliConfigurationNames.StackRuntimeKey}"] = projectRuntime,
            }).Build());
        SetupFeatureResolver features = new(interaction, store, configuration);
        var profiles = Substitute.For<ISetupProfileScopeResolver>();
        profiles.ResolveProfileScopesAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupRenderer>(), Arg.Any<CancellationToken>())
            .Returns([SetupProfileScope.Unconstrained]);
        var bundleReader = Substitute.For<IHostJsonBundleSectionReader>();
        bundleReader.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>()).Returns((HostJsonBundleSection?)null);
        SetupDependencyPlanBuilder planner = new(bundleReader);
        var installer = Substitute.For<ISetupDependencyInstaller>();
        installer.EnsureDependencyAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupDependency>(), Arg.Any<CancellationToken>())
            .Returns(call => (dependencyResult ?? Satisfied)(call.Arg<SetupDependency>()));
        var firstRunStore = Substitute.For<IFirstRunStateStore>();
        // Completion must be guarded by the runner, not depend on a cooperative store.
        firstRunStore.MarkCompleteAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        SetupRunner runner = new(interaction, features, profiles, planner, installer, firstRunStore);
        return new Harness(features, runner, profiles, installer, firstRunStore, interaction, store, configuration);
    }

    private static SetupDependencyResult Satisfied(SetupDependency dependency)
        => SetupDependencyResult.Satisfied(dependency, dependency.PackageId, "1.0.0", "Already installed.");

    private static SetupCommandOptions Options(
        IReadOnlyList<string>? features = null,
        bool assumeYes = false,
        bool json = false,
        bool nonInteractive = false,
        bool check = false)
        => new(
            new DirectoryInfo("setup-automation-contract"),
            features ?? [],
            [],
            Source: null,
            SetupInstallPolicy.IfNeeded,
            IncludePrerelease: false,
            NonInteractive: nonInteractive,
            AssumeYes: assumeYes,
            Check: check,
            OutputMode: json ? SetupOutputMode.Json : SetupOutputMode.Plain);

    private static JsonElement[] ReadEvents(IReadOnlyList<string> lines)
    {
        lines.Should().NotBeEmpty();
        List<JsonElement> events = [];
        foreach (string line in lines)
        {
            using var document = JsonDocument.Parse(line);
            document.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
            EventType(document.RootElement).Should().NotBeNullOrEmpty();
            events.Add(document.RootElement.Clone());
        }

        return [.. events];
    }

    private static string? EventType(JsonElement item) => item.GetProperty("type").GetString();

    private sealed record Harness(
        SetupFeatureResolver Features,
        SetupRunner Runner,
        ISetupProfileScopeResolver Profiles,
        ISetupDependencyInstaller Installer,
        IFirstRunStateStore FirstRunStore,
        SelectionInteraction Interaction,
        IWorkloadStore Store,
        ICliConfigurationProvider Configuration);

    private sealed class SelectionInteraction(bool interactive) : TestInteractionService
    {
        public override bool IsInteractive => interactive;

        public int PromptCount { get; private set; }

        public override async Task<IReadOnlyList<string>> PromptForMultiSelectionAsync(
            string title, IEnumerable<MultiSelectionChoice> choices, CancellationToken cancellationToken = default)
        {
            PromptCount++;
            await base.PromptForMultiSelectionAsync(title, choices, cancellationToken);
            return ["node"];
        }
    }
}