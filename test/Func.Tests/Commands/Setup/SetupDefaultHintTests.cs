// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting.FirstRun;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public sealed class SetupDefaultHintTests
{
    private const string ExpectedHint = "No language stack was selected. Setup targets only the host and extension bundle. "
        + "For language-specific setup, run `func setup --yes --features <stack>`.";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RunAsync_AssumeYesFallback_ExplainsSelectionOnceAcrossProfiles(bool check, bool interactive)
    {
        Fixture fixture = new(interactive: interactive);
        fixture.Profiles.ResolveProfileScopesAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupRenderer>(), Arg.Any<CancellationToken>())
            .Returns([SetupProfileScope.Unconstrained, SetupProfileScope.Unconstrained]);

        SetupRunResult result = await fixture.Runner.RunAsync(Options(check: check), CancellationToken.None);

        result.ExitCode.Should().Be(0);
        fixture.Hints.Should().Equal(ExpectedHint);
        fixture.Dependencies.Should().Equal(SetupDependencyKind.Host, SetupDependencyKind.ExtensionBundle,
            SetupDependencyKind.Host, SetupDependencyKind.ExtensionBundle);
        fixture.Interaction.Received(1).WriteSuccess("Azure Functions setup is complete.");
        await fixture.FirstRun.Received(check ? 0 : 1).MarkCompleteAsync(Arg.Any<CancellationToken>());
        await fixture.Store.DidNotReceive().GetWorkloadsAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("host", null)]
    [InlineData("runtime", null)]
    [InlineData("node", null)]
    [InlineData("runtime", "python")]
    [InlineData(null, "node")]
    [InlineData(null, " dotnet-isolated ")]
    [InlineData(null, "runtime")]
    public async Task RunAsync_ExplicitOrConfiguredFeatures_DoesNotSuggestFallback(string? feature, string? configuredRuntime)
    {
        Fixture fixture = new(configuredRuntime: configuredRuntime);

        SetupRunResult result = await fixture.Runner.RunAsync(
            Options(features: feature is null ? [] : [feature]), CancellationToken.None);

        result.ExitCode.Should().Be(0);
        fixture.Hints.Should().BeEmpty();
        if (feature is not null)
        {
            fixture.Configuration.DidNotReceive().GetProjectConfiguration(Arg.Any<DirectoryInfo>());
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RunAsync_FallbackWithoutAssumeYes_DoesNotAddHint(bool interactive, bool nonInteractive)
    {
        Fixture fixture = new(interactive: interactive);

        SetupRunResult result = await fixture.Runner.RunAsync(
            Options() with { AssumeYes = false, NonInteractive = nonInteractive }, CancellationToken.None);

        result.ExitCode.Should().Be(0);
        fixture.Hints.Should().BeEmpty();
        fixture.Dependencies.Should().Equal(SetupDependencyKind.Host, SetupDependencyKind.ExtensionBundle);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_JsonFallback_PreservesEventsAndDoesNotWriteHint(bool check)
    {
        Fixture fixture = new();

        SetupRunResult result = await fixture.Runner.RunAsync(
            Options(check: check) with { OutputMode = SetupOutputMode.Json }, CancellationToken.None);

        result.ExitCode.Should().Be(0);
        fixture.Hints.Should().BeEmpty();
        JsonElement[] events = [.. fixture.JsonLines.Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        })];
        events.Select(item => item.GetProperty("type").GetString()).Should().Equal(
            "setup.started", "profile.started", "dependency.detected", "dependency.result",
            "dependency.detected", "dependency.result", "profile.completed", "setup.completed");
        events[0].EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "features", "worker_runtimes", "profiles", "source", "install_policy", "check", "prerelease", "type", "timestamp");
        events[0].GetProperty("features").EnumerateArray().Select(item => item.GetString()).Should().Equal("runtime");
        events[0].GetProperty("worker_runtimes").EnumerateArray().Should().BeEmpty();
        events[0].GetProperty("check").GetBoolean().Should().Be(check);
        fixture.Interaction.ReceivedCalls().Should().OnlyContain(call => call.GetMethodInfo().Name == nameof(IInteractionService.WriteRawLine));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_CanceledDuringHint_DoesNotBeginProfilesOrPersistCompletion(bool check)
    {
        using CancellationTokenSource cancellation = new();
        Fixture fixture = new();
        fixture.Interaction.When(interaction => interaction.WriteHint(Arg.Any<string>())).Do(_ => cancellation.Cancel());

        var error = await FluentActions.Awaiting(() => fixture.Runner.RunAsync(Options(check: check), cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        error.Which.CancellationToken.Should().Be(cancellation.Token);
        cancellation.IsCancellationRequested.Should().BeTrue();
        fixture.Interaction.Received(1).WriteHint(ExpectedHint);
        fixture.Profiles.ReceivedCalls().Should().BeEmpty();
        fixture.BundleReader.ReceivedCalls().Should().BeEmpty();
        fixture.Installer.ReceivedCalls().Should().BeEmpty();
        fixture.FirstRun.ReceivedCalls().Should().BeEmpty();
        fixture.Interaction.DidNotReceive().WriteSuccess(Arg.Any<string>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_WhitespaceRuntime_IsAnUnconfiguredFallback(bool check)
    {
        Fixture fixture = new(configuredRuntime: " \t ");

        SetupRunResult result = await fixture.Runner.RunAsync(Options(check: check), CancellationToken.None);

        result.ExitCode.Should().Be(0);
        fixture.Hints.Should().Equal(ExpectedHint);
        fixture.Dependencies.Should().Equal(SetupDependencyKind.Host, SetupDependencyKind.ExtensionBundle);
    }

    [Fact]
    public async Task RunAsync_LocalSettingsRuntimeProjection_DoesNotSuggestFallback()
    {
        DirectoryInfo directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"setup-hint-{Guid.NewGuid():N}"));
        try
        {
            var localSettings = Substitute.For<ILocalSettingsProvider>();
            localSettings.Get(Arg.Any<DirectoryInfo>()).Returns(new LocalSettingsSnapshot
            {
                Values = new Dictionary<string, string> { [CliConfigurationNames.WorkerRuntimeSettingName] = "dotnet-isolated" },
            });
            CliConfigurationProvider configuration = new(localSettings, new CliConfigurationPathsOptions(directory.FullName));
            Fixture fixture = new(configuration: configuration);

            SetupRunResult result = await fixture.Runner.RunAsync(
                Options() with { WorkingDirectory = directory }, CancellationToken.None);

            result.ExitCode.Should().Be(0);
            fixture.Hints.Should().BeEmpty();
            fixture.Dependencies.Should().Equal(SetupDependencyKind.Host, SetupDependencyKind.Stack, SetupDependencyKind.Templates);
            localSettings.Received(1).Get(Arg.Is<DirectoryInfo>(path => path.FullName == directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static SetupCommandOptions Options(IReadOnlyList<string>? features = null, bool check = false)
        => new(new DirectoryInfo("setup-default-hint"), features ?? [], [], Source: null,
            SetupInstallPolicy.IfNeeded, IncludePrerelease: false, NonInteractive: false, AssumeYes: true,
            Check: check, OutputMode: SetupOutputMode.Plain);

    private sealed class Fixture
    {
        public IInteractionService Interaction { get; } = Substitute.For<IInteractionService>();
        public ICliConfigurationProvider Configuration { get; } = Substitute.For<ICliConfigurationProvider>();
        public IWorkloadStore Store { get; } = Substitute.For<IWorkloadStore>();
        public ISetupProfileScopeResolver Profiles { get; } = Substitute.For<ISetupProfileScopeResolver>();
        public IHostJsonBundleSectionReader BundleReader { get; } = Substitute.For<IHostJsonBundleSectionReader>();
        public ISetupDependencyInstaller Installer { get; } = Substitute.For<ISetupDependencyInstaller>();
        public IFirstRunStateStore FirstRun { get; } = Substitute.For<IFirstRunStateStore>();
        public List<string> Hints { get; } = [];
        public List<string> JsonLines { get; } = [];
        public List<SetupDependencyKind> Dependencies { get; } = [];
        public SetupRunner Runner { get; }

        public Fixture(bool interactive = true, string? configuredRuntime = null, ICliConfigurationProvider? configuration = null)
        {
            Interaction.IsInteractive.Returns(interactive);
            Interaction.When(interaction => interaction.WriteHint(Arg.Any<string>())).Do(call => Hints.Add(call.Arg<string>()));
            Interaction.When(interaction => interaction.WriteRawLine(Arg.Any<string>())).Do(call => JsonLines.Add(call.Arg<string>()));
            Interaction.ClearReceivedCalls();
            Configuration.GetProjectConfiguration(Arg.Any<DirectoryInfo>()).Returns(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["stack:runtime"] = configuredRuntime }).Build());
            Store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);
            Profiles.ResolveProfileScopesAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupRenderer>(), Arg.Any<CancellationToken>())
                .Returns([SetupProfileScope.Unconstrained]);
            BundleReader.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>()).Returns((HostJsonBundleSection?)null);
            Installer.EnsureDependencyAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupDependency>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    SetupDependency dependency = call.Arg<SetupDependency>();
                    Dependencies.Add(dependency.Kind);
                    return SetupDependencyResult.Satisfied(dependency, dependency.PackageId, "1.0.0", "Already installed.");
                });
            FirstRun.MarkCompleteAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            SetupFeatureResolver features = new(Interaction, Store, configuration ?? Configuration);
            Runner = new SetupRunner(Interaction, features, Profiles, new SetupDependencyPlanBuilder(BundleReader), Installer, FirstRun);
        }
    }
}