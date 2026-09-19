// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting.FirstRun;
using Azure.Functions.Cli.Profiles;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public sealed class SetupRunnerCancellationTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task RunAsync_RealPlannerReturnsFailureAfterFinalAwait_ObservesCancellation(bool cancel, bool check, bool json)
    {
        using CancellationTokenSource cancellation = new();
        Fixture fixture = new();
        fixture.Features.ResolveFeaturesAsync(Arg.Any<SetupCommandOptions>(), cancellation.Token)
            .Returns(new SetupFeaturePlan(["node"], [new SetupRuntimeFeature("node", "node", true)], ["node"], false));
        var catalog = Substitute.For<ISetupStackCatalog>();
        catalog.GetStacksAsync(Arg.Any<string?>(), Arg.Any<bool>(), cancellation.Token).Returns(new SetupStackSnapshot(
            new Dictionary<string, string> { ["java"] = "contoso.java" }, new Dictionary<string, string>()));
        var reader = Substitute.For<IHostJsonBundleSectionReader>();
        TaskCompletionSource<HostJsonBundleSection?> read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.ReadAsync(Arg.Any<DirectoryInfo>(), cancellation.Token).Returns(read.Task);
        SetupDependencyPlanBuilder planner = new(reader, catalog);
        SetupRunner runner = fixture.Runner(planner);
        Task<SetupRunResult> run = runner.RunAsync(Options(check, json), cancellation.Token);
        run.IsCompleted.Should().BeFalse();
        reader.ReceivedCalls().Should().ContainSingle();

        if (cancel) cancellation.Cancel();
        read.SetResult(null);

        if (cancel)
        {
            await AssertCanceledAsync(run, cancellation, fixture);
            fixture.Installer.ReceivedCalls().Should().BeEmpty();
            fixture.Interaction.Events.Should().NotContain("dependency.detected").And.NotContain("dependency.result")
                .And.NotContain("profile.completed");
            fixture.Interaction.Errors.Should().BeEmpty();
        }
        else
        {
            (await run).ExitCode.Should().Be(1);
            fixture.Installer.ReceivedCalls().Should().HaveCount(check ? 1 : 0);
            if (json) fixture.Interaction.Events.Should().ContainSingle(type => type == "setup.failed");
            else fixture.Interaction.Errors.Should().Contain(message => message.Contains("not available from the selected workload catalog", StringComparison.Ordinal));
        }

        fixture.FirstRun.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RunAsync_DeferredFailureRendering_StopsBeforeTheNextFailureWhenCanceled(bool check, bool cancel)
    {
        using CancellationTokenSource cancellation = new();
        Fixture fixture = new();
        SetupDependencyResult[] failures =
        [
            SetupDependencyResult.Failed(SetupDependency.Runtime("node"), "first planning failure"),
            SetupDependencyResult.Failed(SetupDependency.Runtime("python"), "second planning failure"),
        ];
        fixture.Plans.BuildDependencyPlanAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupFeaturePlan>(),
            Arg.Any<SetupProfileScope>(), cancellation.Token).Returns(new SetupDependencyPlan([SetupDependency.Host(null)], failures));
        fixture.Interaction.AfterEvent = item =>
        {
            if (cancel && item.GetProperty("type").GetString() == "dependency.result"
                && item.GetProperty("status").GetString() == "failed") cancellation.Cancel();
        };

        Task<SetupRunResult> run = fixture.Runner().RunAsync(Options(check), cancellation.Token);

        if (cancel)
        {
            await AssertCanceledAsync(run, cancellation, fixture);
            fixture.Interaction.FailureMessages.Should().Equal("first planning failure");
            fixture.Interaction.Events.Should().NotContain("profile.completed");
        }
        else
        {
            (await run).ExitCode.Should().Be(1);
            fixture.Interaction.FailureMessages.Should().Equal("first planning failure", "second planning failure");
            fixture.Interaction.Events.Should().ContainSingle(type => type == "setup.failed");
        }

        fixture.Installer.ReceivedCalls().Should().HaveCount(check ? 1 : 0);
        fixture.FirstRun.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("features", false)]
    [InlineData("features", true)]
    [InlineData("profiles", false)]
    [InlineData("profiles", true)]
    [InlineData("installer-result", false)]
    [InlineData("installer-result", true)]
    [InlineData("dependency.detected", false)]
    [InlineData("dependency.detected", true)]
    [InlineData("dependency.result", false)]
    [InlineData("dependency.result", true)]
    [InlineData("profile.completed", false)]
    [InlineData("profile.completed", true)]
    public async Task RunAsync_CancellationAtPhaseBoundary_PropagatesWithoutFinalFailure(string boundary, bool check)
    {
        using CancellationTokenSource cancellation = new();
        Fixture fixture = new();
        fixture.Features.ResolveFeaturesAsync(Arg.Any<SetupCommandOptions>(), cancellation.Token).Returns(_ =>
        {
            if (boundary == "features") cancellation.Cancel();
            return Task.FromResult<SetupFeaturePlan?>(new SetupFeaturePlan(["host"], [], [], false));
        });
        fixture.Profiles.ResolveProfileScopesAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupRenderer>(), cancellation.Token)
            .Returns(_ =>
            {
                if (boundary == "profiles") cancellation.Cancel();
                return Task.FromResult<IReadOnlyList<SetupProfileScope>>([SetupProfileScope.Unconstrained]);
            });
        fixture.Installer.EnsureDependencyAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupDependency>(), cancellation.Token)
            .Returns(call =>
            {
                if (boundary == "installer-result") cancellation.Cancel();
                return Task.FromResult(SetupDependencyResult.Failed(call.Arg<SetupDependency>(), "installation failed"));
            });
        fixture.Interaction.AfterEvent = item =>
        {
            if (item.GetProperty("type").GetString() == boundary) cancellation.Cancel();
        };

        await AssertCanceledAsync(fixture.Runner().RunAsync(Options(check), cancellation.Token), cancellation, fixture);

        if (boundary == "features") fixture.Profiles.ReceivedCalls().Should().BeEmpty();
        if (boundary is "features" or "profiles") fixture.Interaction.Events.Should().BeEmpty();
        if (boundary is "features" or "profiles" or "dependency.detected") fixture.Installer.ReceivedCalls().Should().BeEmpty();
        if (boundary == "installer-result") fixture.Interaction.Events.Should().NotContain("dependency.result");
        if (boundary != "profile.completed") fixture.Interaction.Events.Should().NotContain("profile.completed");
    }

    [Theory]
    [InlineData("setup", false)]
    [InlineData("setup", true)]
    [InlineData("profile", false)]
    [InlineData("profile", true)]
    [InlineData("bundle", false)]
    [InlineData("bundle", true)]
    public async Task RunAsync_KnownConfigurationFailure_ReportsErrorUnlessCanceled(string category, bool cancel)
    {
        using CancellationTokenSource cancellation = new();
        Fixture fixture = new();
        Exception failure = category switch
        {
            "setup" => new SetupConfigurationException("invalid setup"),
            "profile" => new ProfileConfigurationException("invalid profile"),
            _ => new ExtensionBundleConfigurationException("invalid bundle"),
        };
        fixture.Plans.BuildDependencyPlanAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupFeaturePlan>(),
            Arg.Any<SetupProfileScope>(), cancellation.Token).Returns(_ =>
            {
                if (cancel) cancellation.Cancel();
                return Task.FromException<SetupDependencyPlan>(failure);
            });

        Task<SetupRunResult> run = fixture.Runner().RunAsync(Options(check: false), cancellation.Token);

        if (cancel) await AssertCanceledAsync(run, cancellation, fixture);
        else
        {
            (await run).ExitCode.Should().Be(1);
            fixture.Interaction.Events.Should().ContainSingle(type => type == "setup.failed");
            fixture.Interaction.Lines.Should().Contain(line => line.Contains(failure.Message, StringComparison.Ordinal));
        }

        fixture.Installer.ReceivedCalls().Should().BeEmpty();
        fixture.FirstRun.ReceivedCalls().Should().BeEmpty();
    }

    private static async Task AssertCanceledAsync(Task<SetupRunResult> run, CancellationTokenSource cancellation, Fixture fixture)
    {
        var failure = await FluentActions.Awaiting(async () => await run).Should().ThrowAsync<OperationCanceledException>();
        failure.Which.CancellationToken.Should().Be(cancellation.Token);
        cancellation.IsCancellationRequested.Should().BeTrue();
        fixture.Interaction.Events.Should().NotContain("setup.failed").And.NotContain("setup.completed");
        fixture.FirstRun.ReceivedCalls().Should().BeEmpty();
    }

    private static SetupCommandOptions Options(bool check, bool json = true)
        => new(new DirectoryInfo("setup-cancellation-tests"), ["host"], [], null, SetupInstallPolicy.IfNeeded,
            false, true, true, check, json ? SetupOutputMode.Json : SetupOutputMode.Plain);

    private sealed class Fixture
    {
        public ISetupFeatureResolver Features { get; } = Substitute.For<ISetupFeatureResolver>();
        public ISetupProfileScopeResolver Profiles { get; } = Substitute.For<ISetupProfileScopeResolver>();
        public ISetupDependencyPlanBuilder Plans { get; } = Substitute.For<ISetupDependencyPlanBuilder>();
        public ISetupDependencyInstaller Installer { get; } = Substitute.For<ISetupDependencyInstaller>();
        public IFirstRunStateStore FirstRun { get; } = Substitute.For<IFirstRunStateStore>();
        public CancelingInteraction Interaction { get; } = new();

        public Fixture()
        {
            Features.ResolveFeaturesAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<CancellationToken>())
                .Returns(new SetupFeaturePlan(["host"], [], [], false));
            Profiles.ResolveProfileScopesAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupRenderer>(), Arg.Any<CancellationToken>())
                .Returns([SetupProfileScope.Unconstrained]);
            Plans.BuildDependencyPlanAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupFeaturePlan>(),
                Arg.Any<SetupProfileScope>(), Arg.Any<CancellationToken>()).Returns(new SetupDependencyPlan([SetupDependency.Host(null)], []));
            Installer.EnsureDependencyAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<SetupDependency>(), Arg.Any<CancellationToken>())
                .Returns(call => SetupDependencyResult.Satisfied(call.Arg<SetupDependency>(), "host", "1.0.0", "Already installed."));
        }

        public SetupRunner Runner(ISetupDependencyPlanBuilder? planner = null)
            => new(Interaction, Features, Profiles, planner ?? Plans, Installer, FirstRun);
    }

    private sealed class CancelingInteraction : TestInteractionService, IInteractionService
    {
        public List<string> Events { get; } = [];
        public List<string> FailureMessages { get; } = [];
        public List<string> Errors { get; } = [];
        public Action<JsonElement>? AfterEvent { get; set; }

        void IInteractionService.WriteRawLine(string text)
        {
            base.WriteRawLine(text);
            using var document = JsonDocument.Parse(text);
            JsonElement item = document.RootElement;
            string type = item.GetProperty("type").GetString()!;
            Events.Add(type);
            if (type == "dependency.result" && item.GetProperty("status").GetString() == "failed")
                FailureMessages.Add(item.GetProperty("message").GetString()!);
            AfterEvent?.Invoke(item);
        }

        void IInteractionService.WriteError(string message)
        {
            base.WriteError(message);
            Errors.Add(message);
        }
    }
}