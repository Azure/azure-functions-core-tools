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
    public async Task ProcessEnvEmptyOrNull_DoesNotBlockLocalSettings()
    {
        SetLocalSettings(new() { ["KeyA"] = "valueA", ["KeyB"] = "valueB" });
        _processEnv.Get("KeyA").Returns((string?)null);
        _processEnv.Get("KeyB").Returns(string.Empty);
        StartInitializationStepContext context = NewContext();

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("valueA", context.State.HostRunContext!.EnvironmentVariables["KeyA"]);
        Assert.Equal("valueB", context.State.HostRunContext!.EnvironmentVariables["KeyB"]);
        _interaction.DidNotReceiveWithAnyArgs().WriteWarning(default!);
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

    private void SetLocalSettings(Dictionary<string, string> values)
    {
        var snapshot = new LocalSettingsSnapshot { Values = values };
        _localSettings.Get(Arg.Any<DirectoryInfo>()).Returns(snapshot);
    }

    private PrepareProjectHostRunInitializationStep NewStep()
        => new(_localSettings, _processEnv, _interaction);

    private StartInitializationStepContext NewContext()
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
            Project = new FakeProject(_projectDir),
            Worker = CreateWorker(),
            ProfileName = "none",
        };
        IStartInitializationRenderer renderer = Substitute.For<IStartInitializationRenderer>();
        IStartInitializationStep stepStub = Substitute.For<IStartInitializationStep>();
        stepStub.Id.Returns("test");
        return new StartInitializationStepContext(init, state, stepStub, renderer, TimeProvider.System);
    }

    private static IFunctionsWorker CreateWorker()
    {
        IFunctionsWorker worker = Substitute.For<IFunctionsWorker>();
        worker.WorkerRuntime.Returns("node");
        worker.WorkerConfigPath.Returns(Path.Combine(Path.GetTempPath(), "node", "worker.config.json"));
        return worker;
    }

    private sealed class FakeProject(string directory) : FunctionsProject
    {
        public override WorkingDirectory WorkingDirectory { get; } = WorkingDirectory.FromExplicit(directory);

        public override string StackName => "node";

        public override string StackDisplayName => "Node.js";

        public override bool SupportsExtensionBundles => true;

        public override FunctionsWorkerReference WorkerReference { get; } = FunctionsWorkerReference.FromWorkload("node");
    }
}
