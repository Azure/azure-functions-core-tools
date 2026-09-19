// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Hosting.FirstRun;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NSubstitute;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public sealed class SetupRunnerSourceValidationTests
{
    public static IEnumerable<object[]> InvalidSourceCases()
    {
        string[] sources = ["./feed", "file:///tmp/feed", "ftp://example.test/feed"];
        string[] features = ["host", "runtime"];
        bool[] flags = [false, true];
        foreach (string source in sources)
        foreach (string feature in features)
        foreach (bool configured in flags)
        foreach (bool check in flags)
        foreach (bool json in flags)
            yield return [source, feature, configured, check, json];
    }

    [Theory]
    [MemberData(nameof(InvalidSourceCases))]
    public async Task RunAsync_IfNeededWithCompatibleInstalledDependencies_DoesNotValidateUnusedSource(
        string source, string feature, bool configured, bool check, bool json)
    {
        var harness = new RunnerHarness(configured ? source : null);
        SetupCommandOptions options = Request(feature, configured ? null : source, check, json);

        SetupRunResult result = await harness.CreateRunner().RunAsync(options, CancellationToken.None);

        result.ExitCode.Should().Be(0);
        harness.AssertNoClientOrInstall();
        await harness.Profiles.Received(1).ResolveProfileScopesAsync(options, Arg.Any<SetupRenderer>(), CancellationToken.None);
        await harness.Store.Received(feature == "host" ? 1 : 2).GetWorkloadsAsync(CancellationToken.None);
        await harness.Marker.Received(1).MarkCompleteAsync(CancellationToken.None);
        if (json)
        {
            JsonElement[] events = ReadEvents(harness.Interaction);
            JsonElement started = events.Should().ContainSingle(item => EventType(item) == "setup.started").Which;
            started.GetProperty("source").GetString().Should().Be(options.Source);
            events.Should().ContainSingle(item => EventType(item) == "setup.completed");
            events.Should().NotContain(item => EventType(item) == "setup.failed");
            events.Where(item => EventType(item) == "dependency.result")
                .Should().HaveCount(feature == "host" ? 1 : 2)
                .And.OnlyContain(item => item.GetProperty("status").GetString() == "satisfied");
        }
        else
        {
            harness.Interaction.Lines.Should().Contain("SUCCESS: Azure Functions setup is complete.");
        }
    }

    [Theory]
    [MemberData(nameof(InvalidSourceCases))]
    public async Task RunAsync_IfNeededWithMissingDependency_ValidatesSourceDuringResolution(
        string source, string feature, bool configured, bool check, bool json)
    {
        var harness = new RunnerHarness(configured ? source : null);
        IReadOnlyList<WorkloadEntry> installed = feature == "host" ? [] : [Installed(SetupDependency.Host(null).PackageId)];
        harness.Store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(installed);
        SetupCommandOptions options = Request(feature, configured ? null : source, check, json);

        SetupRunResult result = await harness.CreateRunner().RunAsync(options, CancellationToken.None);

        result.ExitCode.Should().Be(1);
        FailureMessage(harness.Interaction, json).Should().Contain(source).And.Contain("not a supported NuGet feed");
        harness.AssertNoClientOrInstall();
        await harness.Profiles.Received(1).ResolveProfileScopesAsync(options, Arg.Any<SetupRenderer>(), CancellationToken.None);
        await harness.Store.Received(feature == "host" ? 1 : 2).GetWorkloadsAsync(CancellationToken.None);
        harness.Marker.ReceivedCalls().Should().BeEmpty();
        if (json)
        {
            JsonElement[] events = ReadEvents(harness.Interaction);
            events.Should().ContainSingle(item => EventType(item) == "setup.started");
            events.Last(item => EventType(item) == "dependency.detected").GetProperty("dependency_type").GetString()
                .Should().Be(feature == "host" ? "host" : "extension-bundle");
            events.Where(item => EventType(item) == "dependency.result")
                .Select(item => item.GetProperty("status").GetString())
                .Should().Equal(feature == "host" ? [] : ["satisfied"]);
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData(" \t ", false)]
    [InlineData(" \t ", true)]
    public async Task RunAsync_BlankOverride_UsesConfiguredSourceOnlyWhenResolutionIsNeeded(string? source, bool installed)
    {
        var harness = new RunnerHarness("./invalid-configured-feed");
        if (!installed) harness.Store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);

        SetupRunResult result = await harness.CreateRunner().RunAsync(Request("host", source, true, true), CancellationToken.None);

        result.ExitCode.Should().Be(installed ? 0 : 1);
        if (installed)
        {
            ReadEvents(harness.Interaction).Should().ContainSingle(item => EventType(item) == "setup.completed");
        }
        else
        {
            FailureMessage(harness.Interaction, true).Should().Contain("./invalid-configured-feed");
        }

        harness.AssertNoClientOrInstall();
        await harness.Marker.Received(installed ? 1 : 0).MarkCompleteAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData("host", false)]
    [InlineData("host", true)]
    [InlineData("runtime", false)]
    [InlineData("runtime", true)]
    public async Task RunAsync_LatestCompatibleWithInstalledDependencies_InvalidSourceDoesNotUseFallback(string feature, bool configured)
    {
        const string source = "./invalid-feed";
        var harness = new RunnerHarness(configured ? source : null);
        SetupCommandOptions options = Request(feature, configured ? null : source, false, true) with
        {
            InstallPolicy = SetupInstallPolicy.LatestCompatible,
        };

        SetupRunResult result = await harness.CreateRunner().RunAsync(options, CancellationToken.None);

        result.ExitCode.Should().Be(1);
        FailureMessage(harness.Interaction, true).Should().Contain(source);
        ReadEvents(harness.Interaction).Should().NotContain(item => EventType(item) == "dependency.result");
        harness.AssertNoClientOrInstall();
        harness.Marker.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EnsureDependencyAsync_InvalidSource_PreservesProviderCatalogAndSetupExceptionChain(bool bundle, bool constrained)
    {
        const string source = "./feed[quoted]\"line\nnext";
        var harness = new RunnerHarness(source);
        VersionRange? range = constrained ? VersionRange.Parse("[2.0.0,3.0.0)") : null;
        SetupDependency dependency = bundle
            ? SetupDependency.Bundle(BundleHelpers.StableBundleId, range, range?.ToString(), BundleChannel.Stable)
            : SetupDependency.Host(range);
        SetupCommandOptions options = Request("runtime", null, true, true) with
        {
            InstallPolicy = constrained ? SetupInstallPolicy.IfNeeded : SetupInstallPolicy.LatestCompatible,
        };

        var error = await FluentActions.Awaiting(() => harness.Installer.EnsureDependencyAsync(options, dependency, CancellationToken.None))
            .Should().ThrowAsync<SetupConfigurationException>();

        var catalogError = error.Which.InnerException.Should().BeOfType<InvalidWorkloadSourceException>().Which;
        var providerError = catalogError.InnerException.Should().BeOfType<ArgumentException>().Which;
        providerError.ParamName.Should().Be("value");
        providerError.Message.Should().Contain(source).And.Contain("not a supported NuGet feed");
        catalogError.Message.Should().Be(providerError.Message);
        error.Which.Message.Should().Be(providerError.Message);
        harness.AssertNoClientOrInstall();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_InvalidSourceDiagnostic_PreservesOriginalProviderMessage(bool json)
    {
        const string source = "./feed[quoted]\"line\nnext";
        var harness = new RunnerHarness(source);
        var provider = new PackageSourceProvider(Options.Create(new WorkloadCatalogOptions { Source = source }));
        Exception? original = Record.Exception(() => provider.GetSource());
        original.Should().BeOfType<ArgumentException>();
        SetupCommandOptions options = Request("host", null, false, json) with { InstallPolicy = SetupInstallPolicy.LatestCompatible };

        SetupRunResult result = await harness.CreateRunner().RunAsync(options, CancellationToken.None);

        result.ExitCode.Should().Be(1);
        FailureMessage(harness.Interaction, json).Should().Be(original!.Message);
        harness.AssertNoClientOrInstall();
        harness.Marker.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("  https://offline.test/v3/index.json  ", "./invalid-configured-feed", "https://offline.test/v3/index.json")]
    [InlineData("http://offline.test/v3/index.json", "./invalid-configured-feed", "http://offline.test/v3/index.json")]
    [InlineData(null, "https://configured.test/v3/index.json", "https://configured.test/v3/index.json")]
    [InlineData(" \t ", "https://configured.test/v3/index.json", "https://configured.test/v3/index.json")]
    [InlineData(null, null, PackageSourceProvider.DefaultSourceUrl)]
    public async Task RunAsync_ResolutionWithValidSource_PreservesPrecedenceAndTransportFallback(
        string? source, string? configuredSource, string expectedSource)
    {
        var harness = new RunnerHarness(configuredSource) { ClientFailure = new HttpRequestException("offline") };
        SetupCommandOptions options = Request("host", source, true, true) with { InstallPolicy = SetupInstallPolicy.LatestCompatible };

        SetupRunResult result = await harness.CreateRunner().RunAsync(options, CancellationToken.None);

        result.ExitCode.Should().Be(0);
        PackageSource selected = harness.ClientSources.Should().ContainSingle().Which;
        selected.Source.Should().Be(expectedSource);
        selected.ProtocolVersion.Should().Be(3);
        JsonElement[] events = ReadEvents(harness.Interaction);
        JsonElement dependency = events.Should().ContainSingle(item => EventType(item) == "dependency.result").Which;
        dependency.GetProperty("status").GetString().Should().Be("satisfied-fallback");
        dependency.GetProperty("message").GetString().Should().Contain("offline");
        events.Should().ContainSingle(item => EventType(item) == "setup.completed");
        harness.WorkloadInstaller.ReceivedCalls().Should().BeEmpty();
        await harness.Marker.Received(1).MarkCompleteAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_ClientFactoryBug_IsNotMisclassifiedAsInvalidSourceOrTransportFailure(bool argumentException)
    {
        var harness = new RunnerHarness();
        harness.Store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);
        Exception original = argumentException ? new ArgumentException("Client bug.") : new InvalidOperationException("Client bug.");
        harness.ClientFailure = original;

        Exception? error = await Record.ExceptionAsync(() => harness.CreateRunner().RunAsync(
            Request("host", null, false, true), CancellationToken.None));

        error.Should().BeSameAs(original);
        harness.ClientSources.Should().ContainSingle();
        ReadEvents(harness.Interaction).Should().NotContain(item => EventType(item) == "setup.failed");
        harness.WorkloadInstaller.ReceivedCalls().Should().BeEmpty();
        harness.Marker.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("features")]
    [InlineData("profiles")]
    [InlineData("planner")]
    [InlineData("installer")]
    public async Task RunAsync_DownstreamArgumentException_IsNotCaughtAsInvalidSource(string stage)
    {
        var harness = new RunnerHarness();
        var original = new ArgumentException("Downstream bug.");
        var features = Substitute.For<ISetupFeatureResolver>();
        features.ResolveFeaturesAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<CancellationToken>())
            .Returns(new SetupFeaturePlan(["host"], [], [], false));
        var planner = Substitute.For<ISetupDependencyPlanBuilder>();
        planner.BuildDependencyPlanAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupFeaturePlan>(),
                Arg.Any<SetupProfileScope>(), Arg.Any<CancellationToken>())
            .Returns(new SetupDependencyPlan([SetupDependency.Host(null)], []));
        var installer = Substitute.For<ISetupDependencyInstaller>();
        switch (stage)
        {
            case "features":
                features.ResolveFeaturesAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<CancellationToken>())
                    .Returns<SetupFeaturePlan?>(_ => throw original);
                break;
            case "profiles":
                harness.Profiles.ResolveProfileScopesAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupRenderer>(), Arg.Any<CancellationToken>())
                    .Returns<IReadOnlyList<SetupProfileScope>>(_ => throw original);
                break;
            case "planner":
                planner.BuildDependencyPlanAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupFeaturePlan>(),
                        Arg.Any<SetupProfileScope>(), Arg.Any<CancellationToken>())
                    .Returns<SetupDependencyPlan>(_ => throw original);
                break;
            case "installer":
                installer.EnsureDependencyAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupDependency>(), Arg.Any<CancellationToken>())
                    .Returns<SetupDependencyResult>(_ => throw original);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stage));
        }

        SetupRunner runner = new(harness.Interaction, features, harness.Profiles, planner, installer, harness.Marker);
        Exception? error = await Record.ExceptionAsync(() => runner.RunAsync(Request("host", null, false, true), CancellationToken.None));

        error.Should().BeSameAs(original);
        if (stage is "features" or "profiles") harness.Interaction.Lines.Should().BeEmpty();
        else ReadEvents(harness.Interaction).Should().NotContain(item => EventType(item) == "setup.failed");
        harness.Marker.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_InteractiveStackDiscovery_StillRejectsInvalidSourceBeforeNoSelection(bool configured)
    {
        const string source = "./invalid-feed";
        var harness = new RunnerHarness(configured ? source : null, interactive: true);
        harness.Store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns([.. SetupDependency.BuiltInStackSnapshot.StackPackageIds.Values.Select(Installed)]);
        SetupCommandOptions options = Request("host", configured ? null : source, false, false) with
        {
            Features = [],
            NonInteractive = false,
            AssumeYes = false,
        };

        SetupRunResult result = await harness.CreateRunner().RunAsync(options, CancellationToken.None);

        result.ExitCode.Should().Be(1);
        FailureMessage(harness.Interaction, false).Should().Contain(source);
        harness.AssertNoClientOrInstall();
        harness.Profiles.ReceivedCalls().Should().BeEmpty();
        harness.Store.ReceivedCalls().Should().BeEmpty();
        harness.Marker.ReceivedCalls().Should().BeEmpty();
    }

    private static SetupCommandOptions Request(string feature, string? source, bool check, bool json)
        => new(new DirectoryInfo("setup-runner-source-validation"), [feature], [], source, SetupInstallPolicy.IfNeeded,
            IncludePrerelease: false, NonInteractive: true, AssumeYes: true, check, json ? SetupOutputMode.Json : SetupOutputMode.Plain);

    private static WorkloadEntry Installed(string packageId)
        => new() { PackageId = packageId, PackageVersion = "1.0.0", Kind = WorkloadKind.Workload };

    private static string FailureMessage(TestInteractionService interaction, bool json)
    {
        if (!json)
        {
            return interaction.Lines.Should().ContainSingle(line => line.StartsWith("ERROR: ", StringComparison.Ordinal))
                .Which["ERROR: ".Length..];
        }

        JsonElement[] events = ReadEvents(interaction);
        events.Should().NotContain(item => EventType(item) == "setup.completed" || EventType(item) == "setup.skipped");
        EventType(events[^1]).Should().Be("setup.failed");
        return events.Should().ContainSingle(item => EventType(item) == "setup.failed").Which.GetProperty("message").GetString()!;
    }

    private static JsonElement[] ReadEvents(TestInteractionService interaction)
    {
        interaction.Lines.Should().NotBeEmpty();
        return [.. interaction.Lines.Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        })];
    }

    private static string? EventType(JsonElement item) => item.GetProperty("type").GetString();

    private sealed class RunnerHarness
    {
        public RunnerHarness(string? configuredSource = null, bool interactive = false)
        {
            Interaction = interactive ? new InteractiveInteractionService() : new TestInteractionService();
            var options = Options.Create(new WorkloadCatalogOptions { Source = configuredSource });
            Catalog = new WorkloadCatalog(options, new PackageSourceProvider(options), source =>
            {
                ClientSources.Add(source);
                throw ClientFailure;
            });
            Store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([
                Installed(SetupDependency.Host(null).PackageId),
                Installed(IInstalledBundleWorkloads.BundleWorkloadPackageId),
            ]);
            Profiles.ResolveProfileScopesAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupRenderer>(), Arg.Any<CancellationToken>())
                .Returns([SetupProfileScope.Unconstrained]);
            Installer = new SetupDependencyInstaller(Interaction, Store, Catalog, WorkloadInstaller);
        }

        public TestInteractionService Interaction { get; }
        public WorkloadCatalog Catalog { get; }
        public List<PackageSource> ClientSources { get; } = [];
        public Exception ClientFailure { get; set; } = new InvalidOperationException("Unexpected client creation.");
        public IWorkloadStore Store { get; } = Substitute.For<IWorkloadStore>();
        public IWorkloadInstaller WorkloadInstaller { get; } = Substitute.For<IWorkloadInstaller>();
        public ISetupProfileScopeResolver Profiles { get; } = Substitute.For<ISetupProfileScopeResolver>();
        public IFirstRunStateStore Marker { get; } = Substitute.For<IFirstRunStateStore>();
        public SetupDependencyInstaller Installer { get; }

        public SetupRunner CreateRunner()
        {
            var stacks = new SetupStackCatalog(Catalog);
            var configuration = Substitute.For<ICliConfigurationProvider>();
            configuration.GetProjectConfiguration(Arg.Any<DirectoryInfo>()).Returns(new ConfigurationBuilder().Build());
            var bundles = Substitute.For<IHostJsonBundleSectionReader>();
            bundles.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>()).Returns((HostJsonBundleSection?)null);
            return new SetupRunner(Interaction, new SetupFeatureResolver(Interaction, Store, configuration, stacks), Profiles,
                new SetupDependencyPlanBuilder(bundles, stacks), Installer, Marker);
        }

        public void AssertNoClientOrInstall()
        {
            ClientSources.Should().BeEmpty();
            WorkloadInstaller.ReceivedCalls().Should().BeEmpty();
        }
    }

    private sealed class InteractiveInteractionService : TestInteractionService
    {
        public override bool IsInteractive => true;
    }
}