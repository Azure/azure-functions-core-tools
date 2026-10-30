// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Commands.Start.Host;
using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Commands.Start.Initialization.Rendering;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands.Start.Initialization;

public class PrepareProjectHostRunInitializationStepTests : IDisposable
{
    private const string EncryptedSecretSentinel = "S1_SYNTHETIC_CIPHERTEXT_SENTINEL";
    private const string PythonIsolationSettingName = "PYTHON_ISOLATE_WORKER_DEPENDENCIES";
    private const string PythonExecutableSettingName = "languageWorkers__python__defaultExecutablePath";
    private const string EncryptedSettingsError =
        "Encrypted local.settings.json values are not supported by this Azure Functions CLI path. "
        + "Use a supported decryption or migration workflow to provide plaintext local settings, "
        + "or override the settings through the process environment.";

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

        context.State.HostRunContext!.EnvironmentVariables["MyKey"].Should().Be("MyValue");
        _interaction.DidNotReceiveWithAnyArgs().WriteWarning(default!);
    }

    [Theory]
    [InlineData("PrivateSetting")]
    [InlineData(EncryptedSecretSentinel)]
    public async Task EncryptedValues_FailBeforePreparationWithFixedValueFreeDiagnostic(string settingName)
    {
        string settingsPath = SetEncryptedLocalSettings(new() { [settingName] = EncryptedSecretSentinel });
        byte[] originalBytes = File.ReadAllBytes(settingsPath);
        DateTime originalWriteTime = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(settingsPath, originalWriteTime);
        var project = new FakeProject(_projectDir);
        var renderer = new RecordingRenderer();
        var stackConfigurations = new Dictionary<string, StartHostConfiguration>(StringComparer.OrdinalIgnoreCase)
        {
            ["node"] = new StartHostConfiguration { StartupNotice = "Startup notice must not run." },
        };
        StartInitializationStepContext context = NewContext(project, renderer, stackConfigurations: stackConfigurations);

        Func<Task> action = () => NewStep().ExecuteAsync(context, CancellationToken.None);

        GracefulException exception = (await action.Should().ThrowExactlyAsync<GracefulException>()).Which;
        exception.Message.Should().Be(EncryptedSettingsError);
        exception.IsUserError.Should().BeTrue();
        exception.VerboseMessage.Should().BeNull();
        exception.InnerException.Should().BeNull();
        exception.ToString().Should().NotContain(EncryptedSecretSentinel).And.NotContain(settingName);
        project.PrepareCalled.Should().BeFalse();
        context.State.HostRunContext.Should().BeNull();
        renderer.Events.Should().BeEmpty();
        _interaction.ReceivedCalls().Should().BeEmpty();
        File.ReadAllBytes(settingsPath).Should().Equal(originalBytes);
        File.GetLastWriteTimeUtc(settingsPath).Should().Be(originalWriteTime);
        Directory.Exists(Path.Combine(_projectDir, CliConfigurationNames.ProjectConfigFolderName)).Should().BeFalse();
        Directory.EnumerateFiles(_projectDir, "*", SearchOption.AllDirectories).Should().Equal(settingsPath);
    }

    [Theory]
    [InlineData("fromShell")]
    [InlineData("")]
    public async Task EncryptedValues_ProcessEnvironmentOverrideAllowsPreparation(string processValue)
    {
        SetEncryptedLocalSettings(new()
        {
            ["PrivateSetting"] = EncryptedSecretSentinel,
            ["FUNCTIONS_WORKER_RUNTIME"] = EncryptedSecretSentinel,
        });
        _processEnv.Get("PrivateSetting").Returns(processValue);
        var project = new FakeProject(_projectDir);
        StartInitializationStepContext context = NewContext(project);

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        IDictionary<string, string> environment = context.State.HostRunContext!.EnvironmentVariables;
        environment.ContainsKey("PrivateSetting").Should().BeFalse();
        environment["FUNCTIONS_WORKER_RUNTIME"].Should().Be("node");
        environment.Values.Should().NotContain(EncryptedSecretSentinel);
        project.PrepareCalled.Should().BeTrue();
        _interaction.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task EncryptedValues_PartialEnvironmentOverrideStillFailsBeforePreparation()
    {
        SetEncryptedLocalSettings(new()
        {
            ["FUNCTIONS_WORKER_RUNTIME"] = EncryptedSecretSentinel,
            ["OverriddenSetting"] = EncryptedSecretSentinel,
            ["RequiredSetting"] = EncryptedSecretSentinel,
        });
        _processEnv.Get("FUNCTIONS_WORKER_RUNTIME").Returns("node");
        _processEnv.Get("OverriddenSetting").Returns("fromShell");
        var project = new FakeProject(_projectDir);
        StartInitializationStepContext context = NewContext(project);

        Func<Task> action = () => NewStep().ExecuteAsync(context, CancellationToken.None);

        (await action.Should().ThrowExactlyAsync<GracefulException>()).Which.Message.Should().Be(EncryptedSettingsError);
        project.PrepareCalled.Should().BeFalse();
        context.State.HostRunContext.Should().BeNull();
    }

    [Fact]
    public async Task EncryptedValues_ResolvedHostSourcesAllowPreparationWithoutConsumingCiphertext()
    {
        const string workerDirectoryKey = "languageWorkers__node__workerDirectory";
        SetEncryptedLocalSettings(new()
        {
            ["FUNCTIONS_WORKER_RUNTIME"] = EncryptedSecretSentinel,
            ["HostKey"] = EncryptedSecretSentinel,
            ["BundleKey"] = EncryptedSecretSentinel,
            ["StackKey"] = EncryptedSecretSentinel,
            [workerDirectoryKey] = EncryptedSecretSentinel,
        });
        var stackConfigurations = new Dictionary<string, StartHostConfiguration>(StringComparer.OrdinalIgnoreCase)
        {
            ["node"] = new StartHostConfiguration
            {
                EnvironmentVariables = new Dictionary<string, string> { ["StackKey"] = "fromStack" },
            },
        };
        StartInitializationStepContext context = NewContext(stackConfigurations: stackConfigurations);
        context.State.HostEnvironmentVariables["HostKey"] = "fromHost";
        context.State.BundleEnvVarsForHost["BundleKey"] = "fromBundle";

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        IDictionary<string, string> environment = context.State.HostRunContext!.EnvironmentVariables;
        environment["FUNCTIONS_WORKER_RUNTIME"].Should().Be("node");
        environment["HostKey"].Should().Be("fromHost");
        environment["BundleKey"].Should().Be("fromBundle");
        environment["StackKey"].Should().Be("fromStack");
        environment[workerDirectoryKey].Should().Be(Path.GetDirectoryName(context.State.Worker!.WorkerConfigPath));
        environment.Values.Should().NotContain(EncryptedSecretSentinel);
        _interaction.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(HostProcessStartInfoFactory.ScriptRootEnvironmentVariable)]
    [InlineData(HostProcessStartInfoFactory.AzureFunctionsEnvironmentVariable)]
    [InlineData(HostProcessStartInfoFactory.WebsiteHostnameEnvironmentVariable)]
    [InlineData(HostProcessStartInfoFactory.HostingLifetimeLogLevelEnvironmentVariable)]
    [InlineData("azure_functions_environment")]
    public async Task EncryptedValues_HostLauncherOverridesAllowPreparationWithoutConsumingCiphertext(string settingName)
    {
        SetEncryptedLocalSettings(new() { [settingName] = EncryptedSecretSentinel });
        var project = new FakeProject(_projectDir);
        StartInitializationStepContext context = NewContext(project, demoMode: false);

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        IDictionary<string, string> environment = context.State.HostRunContext!.EnvironmentVariables;
        environment.ContainsKey(settingName).Should().BeFalse();
        environment.Values.Should().NotContain(EncryptedSecretSentinel);
        project.PrepareCalled.Should().BeTrue();
        _interaction.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task EncryptedValues_ProjectPreparationOverridesAllowPreparationWithoutConsumingCiphertext()
    {
        SetEncryptedLocalSettings(new()
        {
            [PythonIsolationSettingName] = EncryptedSecretSentinel,
            [PythonExecutableSettingName] = EncryptedSecretSentinel,
        });
        FakePythonProject project = NewPreparingPythonProject();
        StartInitializationStepContext context = NewContext(project, workerRuntime: "python");

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        project.EnvironmentBeforePreparation!.ContainsKey(PythonIsolationSettingName).Should().BeFalse();
        project.EnvironmentBeforePreparation.ContainsKey(PythonExecutableSettingName).Should().BeFalse();
        project.EnvironmentBeforePreparation.Values.Should().NotContain(EncryptedSecretSentinel);
        IDictionary<string, string> environment = context.State.HostRunContext!.EnvironmentVariables;
        environment[PythonIsolationSettingName].Should().Be("1");
        environment[PythonExecutableSettingName].Should().Be(Path.Combine(_projectDir, ".venv", "safe-python"));
        environment.Values.Should().NotContain(EncryptedSecretSentinel);
        project.PrepareCalled.Should().BeTrue();
        _interaction.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, "PrivateSetting")]
    [InlineData(true, "PrivateSetting")]
    [InlineData(true, "FUNCTIONS_WORKER_RUNTIME_VERSION")]
    public async Task EncryptedValues_GuaranteedOverrideDoesNotHideUnresolvedSetting(bool pythonPreparation, string unresolvedSetting)
    {
        string guaranteedSetting = pythonPreparation
            ? PythonIsolationSettingName
            : HostProcessStartInfoFactory.AzureFunctionsEnvironmentVariable;
        SetEncryptedLocalSettings(new()
        {
            [guaranteedSetting] = EncryptedSecretSentinel,
            [unresolvedSetting] = EncryptedSecretSentinel,
        });
        FakeProject project = pythonPreparation ? NewPreparingPythonProject() : new FakeProject(_projectDir);
        var renderer = new RecordingRenderer();
        var stackConfigurations = new Dictionary<string, StartHostConfiguration>(StringComparer.OrdinalIgnoreCase)
        {
            [project.StackName] = new StartHostConfiguration { StartupNotice = "Startup notice must not run." },
        };
        StartInitializationStepContext context = NewContext(project, renderer, stackConfigurations: stackConfigurations,
            workerRuntime: pythonPreparation ? "python" : "node", demoMode: false);

        Func<Task> action = () => NewStep().ExecuteAsync(context, CancellationToken.None);

        (await action.Should().ThrowExactlyAsync<GracefulException>()).Which.Message.Should().Be(EncryptedSettingsError);
        project.PrepareCalled.Should().BeFalse();
        context.State.HostRunContext.Should().BeNull();
        renderer.Events.Should().BeEmpty();
        _interaction.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(HostProcessStartInfoFactory.AspNetCoreSuppressStatusMessagesEnvironmentVariable)]
    [InlineData(PythonIsolationSettingName)]
    [InlineData(PythonExecutableSettingName)]
    public async Task EncryptedValues_WithoutApplicableGuaranteeStillFailBeforePreparation(string settingName)
    {
        SetEncryptedLocalSettings(new() { [settingName] = EncryptedSecretSentinel });
        var project = new FakeProject(_projectDir);
        StartInitializationStepContext context = NewContext(project, demoMode: false);

        Func<Task> action = () => NewStep().ExecuteAsync(context, CancellationToken.None);

        (await action.Should().ThrowExactlyAsync<GracefulException>()).Which.Message.Should().Be(EncryptedSettingsError);
        project.PrepareCalled.Should().BeFalse();
        context.State.HostRunContext.Should().BeNull();
    }

    [Fact]
    public async Task EncryptedValues_LauncherGuaranteeDoesNotApplyToDemoMode()
    {
        SetEncryptedLocalSettings(new()
        {
            [HostProcessStartInfoFactory.AzureFunctionsEnvironmentVariable] = EncryptedSecretSentinel,
        });
        var project = new FakeProject(_projectDir);
        StartInitializationStepContext context = NewContext(project);

        Func<Task> action = () => NewStep().ExecuteAsync(context, CancellationToken.None);

        (await action.Should().ThrowExactlyAsync<GracefulException>()).Which.Message.Should().Be(EncryptedSettingsError);
        project.PrepareCalled.Should().BeFalse();
        context.State.HostRunContext.Should().BeNull();
    }

    [Fact]
    public async Task EncryptedFileWithoutValues_DoesNotBlockPreparation()
    {
        SetEncryptedLocalSettings([]);
        var project = new FakeProject(_projectDir);
        StartInitializationStepContext context = NewContext(project);

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        project.PrepareCalled.Should().BeTrue();
        context.State.HostRunContext!.EnvironmentVariables["FUNCTIONS_WORKER_RUNTIME"].Should().Be("node");
        _interaction.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task EmptyKey_IsSkippedAndWarns()
    {
        SetLocalSettings(new() { [string.Empty] = "anything" });
        StartInitializationStepContext context = NewContext();

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        context.State.HostRunContext!.EnvironmentVariables.ContainsKey(string.Empty).Should().BeFalse();
        _interaction.Received(1).WriteWarning(Arg.Is<string>(m => m.Contains("empty key", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task EmptyStringValue_IsPreserved()
    {
        SetLocalSettings(new() { ["ClearMe"] = string.Empty });
        StartInitializationStepContext context = NewContext();

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        context.State.HostRunContext!.EnvironmentVariables.TryGetValue("ClearMe", out string? value).Should().BeTrue();
        value.Should().Be(string.Empty);
        _interaction.DidNotReceiveWithAnyArgs().WriteWarning(default!);
    }

    [Fact]
    public async Task ProcessEnvSet_OverridesLocalSettings_AndWarns()
    {
        SetLocalSettings(new() { ["MyKey"] = "fromFile" });
        _processEnv.Get("MyKey").Returns("fromShell");
        StartInitializationStepContext context = NewContext();

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        context.State.HostRunContext!.EnvironmentVariables.ContainsKey("MyKey").Should().BeFalse();
        _interaction.Received(1).WriteWarning(Arg.Is<string>(m => m.Contains("MyKey") && m.Contains("already set", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task ProcessEnvSet_IsCaseInsensitive()
    {
        SetLocalSettings(new() { ["MYKEY"] = "fromFile" });
        _processEnv.Get("MYKEY").Returns("fromShell");
        StartInitializationStepContext context = NewContext();

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        context.State.HostRunContext!.EnvironmentVariables.ContainsKey("MYKEY").Should().BeFalse();
        context.State.HostRunContext!.EnvironmentVariables.ContainsKey("MyKey").Should().BeFalse();
    }

    [Fact]
    public async Task ProcessEnvNull_DoesNotBlockLocalSettings()
    {
        SetLocalSettings(new() { ["KeyA"] = "valueA" });
        _processEnv.Get("KeyA").Returns((string?)null);
        StartInitializationStepContext context = NewContext();

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        context.State.HostRunContext!.EnvironmentVariables["KeyA"].Should().Be("valueA");
        _interaction.DidNotReceiveWithAnyArgs().WriteWarning(default!);
    }

    [Fact]
    public async Task ProcessEnvEmptyString_BlocksLocalSettings()
    {
        SetLocalSettings(new() { ["KeyB"] = "valueB" });
        _processEnv.Get("KeyB").Returns(string.Empty);
        StartInitializationStepContext context = NewContext();

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        context.State.HostRunContext!.EnvironmentVariables.ContainsKey("KeyB").Should().BeFalse();
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
        env["HostKey"].Should().Be("fromHost");
        env["BundleKey"].Should().Be("fromBundle");
        env.ContainsKey("MyKey").Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WiresReporterToProjectContext()
    {
        SetLocalSettings([]);
        var renderer = new RecordingRenderer();
        var project = new ReportingProject(_projectDir);
        StartInitializationStepContext context = NewContext(project, renderer);

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        context.State.HostRunContext!.Reporter.Should().NotBeNull();
        renderer.Events.Any(ev => ev is StartInitializationProgressEvent progress
            && progress.StepId == PrepareProjectHostRunInitializationStep.StepId
            && double.IsNaN(progress.Percent)
            && progress.Message == "running npm install").Should().BeTrue();
        renderer.Events.Any(ev => ev is StartInitializationLogEvent log
            && log.StepId == PrepareProjectHostRunInitializationStep.StepId
            && log.Line == "npm install output"
            && log.Severity == FunctionsProjectReportSeverity.Warning).Should().BeTrue();
    }

    [Fact]
    public async Task Reporter_ForwardsStatusAndLog()
    {
        var renderer = new RecordingRenderer();
        StartInitializationStepContext context = NewContext(renderer: renderer, stepId: "adapter_step");
        await using var reporter = new FunctionsProjectHostRunReporter(context, CancellationToken.None);

        reporter.ReportStatus("building");
        reporter.WriteLog("build output", FunctionsProjectReportSeverity.Error);
        await reporter.CompleteAsync();

        renderer.Events.Any(ev => ev is StartInitializationProgressEvent progress
            && progress.StepId == "adapter_step"
            && double.IsNaN(progress.Percent)
            && progress.Message == "building").Should().BeTrue();
        renderer.Events.Any(ev => ev is StartInitializationLogEvent log
            && log.StepId == "adapter_step"
            && log.Line == "build output"
            && log.Severity == FunctionsProjectReportSeverity.Error).Should().BeTrue();
    }

    [Fact]
    public async Task StackHostConfiguration_ForMatchingStack_IsMergedWithHighestPriority()
    {
        SetLocalSettings(new() { ["SharedKey"] = "fromFile" });
        var stackConfigurations = new Dictionary<string, StartHostConfiguration>(StringComparer.OrdinalIgnoreCase)
        {
            ["node"] = new StartHostConfiguration
            {
                EnvironmentVariables = new Dictionary<string, string>
                {
                    ["FUNCTIONS_CORETOOLS_DEFER_ENV__DOTNET_STARTUP_HOOKS"] = "Microsoft.Azure.Functions.Worker.Core",
                    ["SharedKey"] = "fromStack",
                },
            },
        };
        StartInitializationStepContext context = NewContext(stackConfigurations: stackConfigurations);

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        IDictionary<string, string> env = context.State.HostRunContext!.EnvironmentVariables;
        env["FUNCTIONS_CORETOOLS_DEFER_ENV__DOTNET_STARTUP_HOOKS"].Should().Be("Microsoft.Azure.Functions.Worker.Core");
        env["SharedKey"].Should().Be("fromStack");
    }

    [Fact]
    public async Task StackHostConfiguration_ForNonMatchingStack_IsIgnored()
    {
        SetLocalSettings([]);
        var stackConfigurations = new Dictionary<string, StartHostConfiguration>(StringComparer.OrdinalIgnoreCase)
        {
            ["dotnet"] = new StartHostConfiguration
            {
                EnvironmentVariables = new Dictionary<string, string> { ["FUNCTIONS_ENABLE_DEBUGGER_WAIT"] = "True" },
            },
        };
        StartInitializationStepContext context = NewContext(stackConfigurations: stackConfigurations);

        await NewStep().ExecuteAsync(context, CancellationToken.None);

        context.State.HostRunContext!.EnvironmentVariables.ContainsKey("FUNCTIONS_ENABLE_DEBUGGER_WAIT").Should().BeFalse();
    }

    private string SetEncryptedLocalSettings(Dictionary<string, string> values)
    {
        string path = Path.Combine(_projectDir, CliConfigurationNames.LocalSettingsFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(new { IsEncrypted = true, Values = values }));
        LocalSettingsSnapshot snapshot = new LocalSettingsProvider().Get(new DirectoryInfo(_projectDir));
        _localSettings.Get(Arg.Any<DirectoryInfo>()).Returns(snapshot);
        return path;
    }

    private void SetLocalSettings(Dictionary<string, string> values)
    {
        var snapshot = new LocalSettingsSnapshot { Values = values };
        _localSettings.Get(Arg.Any<DirectoryInfo>()).Returns(snapshot);
    }

    private PrepareProjectHostRunInitializationStep NewStep()
        => new(_localSettings, _processEnv, _interaction);

    private FakePythonProject NewPreparingPythonProject()
        => new(_projectDir, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [PythonIsolationSettingName] = "1",
            [PythonExecutableSettingName] = Path.Combine(_projectDir, ".venv", "safe-python"),
        });

    private StartInitializationStepContext NewContext(
        FunctionsProject? project = null,
        IStartInitializationRenderer? renderer = null,
        string? stepId = null,
        IReadOnlyDictionary<string, StartHostConfiguration>? stackConfigurations = null,
        string workerRuntime = "node",
        bool demoMode = true)
    {
        var options = new StartCommandOptions(
            WorkingDirectory.FromExplicit(_projectDir),
            Port: null, Cors: [], CorsCredentials: false, Functions: [],
            NoBuild: false, EnableAuth: false, RequestedProfileName: null, RequestedHostVersion: null,
            Offline: false, OutputMode: OutputMode.Plain, NoTui: true, LogFilePath: null,
            DemoMode: demoMode, DemoFunctionCount: 0, DemoSpeedMultiplier: 0.001, DemoAutoExit: true,
            StackHostConfigurations: stackConfigurations);

        var init = new StartInitializationContext(options, "5.0.0-test", IsInteractive: false, CanPrompt: false);
        var state = new StartInitializationState
        {
            Project = project ?? new FakeProject(_projectDir),
            Worker = CreateWorker(workerRuntime),
            ProfileName = "none",
        };
        renderer ??= Substitute.For<IStartInitializationRenderer>();
        IStartInitializationStep stepStub = Substitute.For<IStartInitializationStep>();
        stepStub.Id.Returns(stepId ?? PrepareProjectHostRunInitializationStep.StepId);
        return new StartInitializationStepContext(init, state, stepStub, renderer, TimeProvider.System);
    }

    private static IFunctionsWorker CreateWorker(string workerRuntime)
    {
        IFunctionsWorker worker = Substitute.For<IFunctionsWorker>();
        worker.WorkerRuntime.Returns(workerRuntime);
        worker.WorkerConfigPath.Returns(Path.Combine(Path.GetTempPath(), workerRuntime, "worker.config.json"));
        return worker;
    }

    private class FakeProject(string directory) : FunctionsProject
    {
        public bool PrepareCalled { get; private set; }

        public override WorkingDirectory WorkingDirectory { get; } = WorkingDirectory.FromExplicit(directory);

        public override string StackName => "node";

        public override string StackDisplayName => "Node.js";

        public override bool SupportsExtensionBundles => true;

        public override FunctionsWorkerReference WorkerReference { get; } = FunctionsWorkerReference.FromWorkload("node");

        public override Task PrepareForHostRunAsync(FunctionsProjectHostRunContext context, CancellationToken cancellationToken)
        {
            PrepareCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePythonProject(string directory, IReadOnlyDictionary<string, string> overrides) : FakeProject(directory)
    {
        public IReadOnlyDictionary<string, string>? EnvironmentBeforePreparation { get; private set; }

        public override string StackName => "python";

        public override bool GuaranteesHostRunEnvironmentVariableOverride(string name) => overrides.ContainsKey(name);

        public override Task PrepareForHostRunAsync(FunctionsProjectHostRunContext context, CancellationToken cancellationToken)
        {
            EnvironmentBeforePreparation = new Dictionary<string, string>(context.EnvironmentVariables);
            foreach ((string name, string value) in overrides)
            {
                context.EnvironmentVariables[name] = value;
            }

            return base.PrepareForHostRunAsync(context, cancellationToken);
        }
    }

    private sealed class ReportingProject(string directory) : FakeProject(directory)
    {
        public override Task PrepareForHostRunAsync(FunctionsProjectHostRunContext context, CancellationToken cancellationToken)
        {
            context.Reporter.ReportStatus("running npm install");
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
