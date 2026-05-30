// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Commands.Start.Initialization.Rendering;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Start.Initialization;

public class PrepareProjectHostRunInitializationStepTests : IDisposable
{
    private readonly string _projectDir;
    private readonly IInteractionService _interaction = Substitute.For<IInteractionService>();
    private readonly IProcessEnvironment _processEnv = Substitute.For<IProcessEnvironment>();
    private readonly ILocalSettingsProvider _localSettings = Substitute.For<ILocalSettingsProvider>();

    public PrepareProjectHostRunInitializationStepTests()
    {
        _projectDir = Path.Combine(Path.GetTempPath(), "prep-host-step-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectDir);
        _processEnv.Get(Arg.Any<string>()).Returns((string?)null);
    }

    public void Dispose()
    {
        if (Directory.Exists(_projectDir))
        {
            Directory.Delete(_projectDir, recursive: true);
        }
    }

    [Fact]
    public async Task PopulatesEnvFromLocalSettings()
    {
        SetLocalSettings(new() { ["MyKey"] = "MyValue" });
        StartInitializationStepContext context = NewContext();

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("MyValue", context.State.HostRunContext!.EnvironmentVariables["MyKey"]);
        _interaction.DidNotReceiveWithAnyArgs().WriteWarning(default!);
    }

    [Fact]
    public async Task EmptyKey_IsSkippedAndWarns()
    {
        SetLocalSettings(new() { [string.Empty] = "anything" });
        StartInitializationStepContext context = NewContext();

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        Assert.False(context.State.HostRunContext!.EnvironmentVariables.ContainsKey(string.Empty));
        _interaction.Received(1).WriteWarning(Arg.Is<string>(m => m.Contains("empty key", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task EmptyStringValue_IsPreserved()
    {
        SetLocalSettings(new() { ["ClearMe"] = string.Empty });
        StartInitializationStepContext context = NewContext();

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        Assert.True(context.State.HostRunContext!.EnvironmentVariables.TryGetValue("ClearMe", out string? value));
        Assert.Equal(string.Empty, value);
        _interaction.DidNotReceiveWithAnyArgs().WriteWarning(default!);
    }

    [Fact]
    public async Task ProcessEnvSet_OverridesLocalSettings_AndWarns()
    {
        SetLocalSettings(new() { ["MyKey"] = "fromFile" });
        _processEnv.Get("MyKey").Returns("fromShell");
        StartInitializationStepContext context = NewContext();

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        Assert.False(context.State.HostRunContext!.EnvironmentVariables.ContainsKey("MyKey"));
        _interaction.Received(1).WriteWarning(Arg.Is<string>(m => m.Contains("MyKey") && m.Contains("already set", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task ProcessEnvSet_IsCaseInsensitive()
    {
        SetLocalSettings(new() { ["MYKEY"] = "fromFile" });
        _processEnv.Get("MYKEY").Returns("fromShell");
        StartInitializationStepContext context = NewContext();

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        Assert.False(context.State.HostRunContext!.EnvironmentVariables.ContainsKey("MYKEY"));
        Assert.False(context.State.HostRunContext!.EnvironmentVariables.ContainsKey("MyKey"));
    }

    [Fact]
    public async Task ProcessEnvNull_DoesNotBlockLocalSettings()
    {
        SetLocalSettings(new() { ["KeyA"] = "valueA" });
        _processEnv.Get("KeyA").Returns((string?)null);
        StartInitializationStepContext context = NewContext();

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("valueA", context.State.HostRunContext!.EnvironmentVariables["KeyA"]);
        _interaction.DidNotReceiveWithAnyArgs().WriteWarning(default!);
    }

    [Fact]
    public async Task ProcessEnvEmptyString_BlocksLocalSettings()
    {
        SetLocalSettings(new() { ["KeyB"] = "valueB" });
        _processEnv.Get("KeyB").Returns(string.Empty);
        StartInitializationStepContext context = NewContext();

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        Assert.False(context.State.HostRunContext!.EnvironmentVariables.ContainsKey("KeyB"));
        _interaction.Received(1).WriteWarning(Arg.Is<string>(s => s.Contains("KeyB")));
    }

    [Fact]
    public async Task ProcessEnvPrecedence_DoesNotAffectOtherSources()
    {
        SetLocalSettings(new() { ["MyKey"] = "fromFile" });
        _processEnv.Get("MyKey").Returns("fromShell");
        _processEnv.Get("HostKey").Returns("fromShell");
        _processEnv.Get("BundleKey").Returns("fromShell");

        StartInitializationStepContext context = NewContext();
        context.State.HostEnvironmentVariables["HostKey"] = "fromHost";
        context.State.BundleEnvVarsForHost["BundleKey"] = "fromBundle";

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        IDictionary<string, string> env = context.State.HostRunContext!.EnvironmentVariables;
        Assert.Equal("fromHost", env["HostKey"]);
        Assert.Equal("fromBundle", env["BundleKey"]);
        Assert.False(env.ContainsKey("MyKey"));
    }

    [Fact]
    public async Task ExecuteAsync_WiresReporterToProjectContext()
    {
        SetLocalSettings([]);
        var renderer = new RecordingRenderer();
        var project = new ReportingProject(_projectDir);
        StartInitializationStepContext context = NewContext(project, renderer);

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        Assert.NotNull(context.State.HostRunContext!.Reporter);
        Assert.Contains(renderer.Events, ev => ev is StartInitializationProgressEvent progress
            && progress.StepId == PrepareProjectHostRunInitializationStep.StepId
            && double.IsNaN(progress.Percent)
            && progress.Message == "running npm install");
        Assert.Contains(renderer.Events, ev => ev is StartInitializationProgressEvent progress
            && progress.StepId == PrepareProjectHostRunInitializationStep.StepId
            && progress.Percent == 25
            && progress.Message == "installing");
        Assert.Contains(renderer.Events, ev => ev is StartInitializationLogEvent log
            && log.StepId == PrepareProjectHostRunInitializationStep.StepId
            && log.Line == "npm install output"
            && log.Severity == FunctionsProjectReportSeverity.Warning);
    }

    [Fact]
    public async Task Reporter_ForwardsStatusProgressAndLog()
    {
        var renderer = new RecordingRenderer();
        StartInitializationStepContext context = NewContext(renderer: renderer, stepId: "adapter_step");
        await using var reporter = new FunctionsProjectHostRunReporter(context, CancellationToken.None);

        reporter.ReportStatus("building");
        reporter.ReportProgress(75, "almost done");
        reporter.WriteLog("build output", FunctionsProjectReportSeverity.Error);
        await reporter.CompleteAsync();

        Assert.Contains(renderer.Events, ev => ev is StartInitializationProgressEvent progress
            && progress.StepId == "adapter_step"
            && double.IsNaN(progress.Percent)
            && progress.Message == "building");
        Assert.Contains(renderer.Events, ev => ev is StartInitializationProgressEvent progress
            && progress.StepId == "adapter_step"
            && progress.Percent == 75
            && progress.Message == "almost done");
        Assert.Contains(renderer.Events, ev => ev is StartInitializationLogEvent log
            && log.StepId == "adapter_step"
            && log.Line == "build output"
            && log.Severity == FunctionsProjectReportSeverity.Error);
    }

    private void SetLocalSettings(Dictionary<string, string> values)
    {
        var snapshot = new LocalSettingsSnapshot { Values = values };
        _localSettings.Get(Arg.Any<DirectoryInfo>()).Returns(snapshot);
    }

    private PrepareProjectHostRunInitializationStep NewStep()
        => new(_localSettings, _processEnv, _interaction);

    private StartInitializationStepContext NewContext(
        FunctionsProject? project = null,
        IStartInitializationRenderer? renderer = null,
        string? stepId = null)
    {
        var options = new StartCommandOptions(
            WorkingDirectory.FromExplicit(_projectDir),
            Port: null, Cors: [], CorsCredentials: false, Functions: [],
            NoBuild: false, EnableAuth: false, RequestedProfileName: null, RequestedHostVersion: null,
            Offline: false, OutputMode: OutputMode.Plain, NoTui: true, LogFilePath: null,
            DemoMode: true, DemoFunctionCount: 0, DemoSpeedMultiplier: 0.001, DemoAutoExit: true);

        var init = new StartInitializationContext(options, "5.0.0-test", IsInteractive: false, CanPrompt: false);
        var state = new StartInitializationState
        {
            Project = project ?? new FakeProject(_projectDir),
            Worker = CreateWorker(),
            ProfileName = "none",
        };
        renderer ??= Substitute.For<IStartInitializationRenderer>();
        IStartInitializationStep stepStub = Substitute.For<IStartInitializationStep>();
        stepStub.Id.Returns(stepId ?? PrepareProjectHostRunInitializationStep.StepId);
        return new StartInitializationStepContext(init, state, stepStub, renderer, TimeProvider.System);
    }

    private static IFunctionsWorker CreateWorker()
    {
        IFunctionsWorker worker = Substitute.For<IFunctionsWorker>();
        worker.WorkerRuntime.Returns("node");
        worker.WorkerConfigPath.Returns(Path.Combine(Path.GetTempPath(), "node", "worker.config.json"));
        return worker;
    }

    private class FakeProject(string directory) : FunctionsProject
    {
        public override WorkingDirectory WorkingDirectory { get; } = WorkingDirectory.FromExplicit(directory);

        public override string StackName => "node";

        public override string StackDisplayName => "Node.js";

        public override bool SupportsExtensionBundles => true;

        public override FunctionsWorkerReference WorkerReference { get; } = FunctionsWorkerReference.FromWorkload("node");
    }

    private sealed class ReportingProject(string directory) : FakeProject(directory)
    {
        public override Task PrepareForHostRunAsync(FunctionsProjectHostRunContext context, CancellationToken cancellationToken)
        {
            context.Reporter.ReportStatus("running npm install");
            context.Reporter.ReportProgress(25, "installing");
            context.Reporter.WriteLog("npm install output", FunctionsProjectReportSeverity.Warning);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRenderer : IStartInitializationRenderer
    {
        public List<StartInitializationEvent> Events { get; } = [];

        public Task OnEventAsync(StartInitializationEvent initializationEvent, CancellationToken cancellationToken)
        {
            Events.Add(initializationEvent);
            return Task.CompletedTask;
        }

        public Task<bool> ConfirmAsync(string prompt, bool defaultValue, CancellationToken cancellationToken)
            => Task.FromResult(defaultValue);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
