// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Commands.Start.Initialization.Rendering;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Demo;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using NSubstitute;
using Spectre.Console;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class StartInitializationTests : IDisposable
{
    private readonly string _tempDir;

    public StartInitializationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-start-init-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task DemoRunner_SimulatesHostInstallAndSkipsBundle_ForDotNet()
    {
        IFunctionsProjectResolver projectResolver = Substitute.For<IFunctionsProjectResolver>();
        IFunctionsProject project = CreateProject(
            WorkingDirectory.FromExplicit(_tempDir),
            stackName: "dotnet-isolated",
            stackDisplayName: ".NET",
            supportsExtensionBundles: false);
        projectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Resolved(project, "found .csproj"));

        var renderer = new RecordingStartInitializationRenderer();
        var runner = new DemoStartInitializationRunner(projectResolver);
        StartInitializationContext context = CreateContext(
            WorkingDirectory.FromExplicit(_tempDir),
            cliVersion: "5.0.0-test",
            demoFunctionCount: 12,
            demoSpeedMultiplier: 0.001,
            demoAutoExit: true);

        StartInitializationResult result = await runner.RunAsync(context, renderer, CancellationToken.None);

        Assert.Equal(".NET", result.RunInfo.StackName);
        Assert.Equal("4.834.0", result.HostVersion);
        Assert.False(result.BundleRequired);
        Assert.IsType<DemoEventSource>(result.EventStream);
        Assert.Contains(renderer.Events, static ev =>
            ev is StartInitializationStepStartedEvent
            {
                Step:
                {
                    Id: InstallHostWorkloadInitializationStep.StepId,
                    DisplayKind: StartInitializationDisplayKind.Progress,
                },
            });
        Assert.Contains(renderer.Events, static ev =>
            ev is StartInitializationProgressEvent
            {
                StepId: InstallHostWorkloadInitializationStep.StepId,
                Percent: 100,
            });
    }

    [Fact]
    public async Task DemoRunner_HostValidationAddsInstallStepBeforeStackResolution()
    {
        IFunctionsProjectResolver projectResolver = Substitute.For<IFunctionsProjectResolver>();
        IFunctionsProject project = CreateProject(WorkingDirectory.FromExplicit(_tempDir));
        projectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Resolved(project, "found .csproj"));
        var renderer = new RecordingStartInitializationRenderer();
        var runner = new DemoStartInitializationRunner(projectResolver);
        StartInitializationContext context = CreateContext(
            WorkingDirectory.FromExplicit(_tempDir),
            cliVersion: "5.0.0-test",
            demoFunctionCount: 12,
            demoSpeedMultiplier: 0.001,
            demoAutoExit: true);

        await runner.RunAsync(context, renderer, CancellationToken.None);

        int validationCompletedIndex = FindCompletedStepIndex(renderer.Events, ValidateHostWorkloadInitializationStep.StepId);
        int installStartedIndex = FindStartedStepIndex(renderer.Events, InstallHostWorkloadInitializationStep.StepId);
        int stackStartedIndex = FindStartedStepIndex(renderer.Events, ResolveFunctionsProjectInitializationStep.StepId);

        Assert.True(validationCompletedIndex >= 0);
        Assert.True(installStartedIndex > validationCompletedIndex);
        Assert.True(stackStartedIndex > installStartedIndex);
    }

    [Fact]
    public async Task JsonRenderer_EmitsInitializationRecords()
    {
        using var stream = new MemoryStream();
        var renderer = new JsonStartInitializationRenderer(stream, ownsStream: false);
        const string customStepId = "custom_step";

        await renderer.OnEventAsync(
            new StartInitializationStartedEvent(DateTimeOffset.UnixEpoch, "none"),
            CancellationToken.None);
        await renderer.OnEventAsync(
            new StartInitializationProgressEvent(DateTimeOffset.UnixEpoch, customStepId, 50, "Downloading"),
            CancellationToken.None);
        await renderer.DisposeAsync();

        string[] lines = Encoding.UTF8.GetString(stream.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        using var started = JsonDocument.Parse(lines[0]);
        Assert.Equal("start_initialization_started", started.RootElement.GetProperty("kind").GetString());
        Assert.Equal("none", started.RootElement.GetProperty("profile").GetString());
        using var progress = JsonDocument.Parse(lines[1]);
        Assert.Equal("start_initialization_progress", progress.RootElement.GetProperty("kind").GetString());
        Assert.Equal(customStepId, progress.RootElement.GetProperty("step").GetString());
        Assert.Equal(50, progress.RootElement.GetProperty("percent").GetDouble());
    }

    [Fact]
    public async Task CompactRenderer_RendersChecklistLines()
    {
        using var writer = new StringWriter();
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.Yes,
            Out = new TestTerminalOutput(writer),
        });
        var renderer = new CompactStartInitializationRenderer(new TestInteractionService(), "5.0.0-test", console);

        await renderer.OnEventAsync(new StartInitializationStartedEvent(DateTimeOffset.UnixEpoch, "none"), CancellationToken.None);
        await renderer.OnEventAsync(
            new StartInitializationStepStartedEvent(
                 DateTimeOffset.UnixEpoch,
                 new StartInitializationStep(ResolveProfileInitializationStep.StepId, "Resolve profile")),
              CancellationToken.None);
        await Task.Delay(500);
        await renderer.OnEventAsync(
            new StartInitializationStepCompletedEvent(DateTimeOffset.UnixEpoch, ResolveProfileInitializationStep.StepId, "none"),
            CancellationToken.None);
        await renderer.OnEventAsync(
            new StartInitializationStepStartedEvent(
                DateTimeOffset.UnixEpoch,
                new StartInitializationStep(
                    InstallHostWorkloadInitializationStep.StepId,
                    "Install host workload",
                    DisplayKind: StartInitializationDisplayKind.Progress)),
            CancellationToken.None);
        await renderer.OnEventAsync(
            new StartInitializationProgressEvent(DateTimeOffset.UnixEpoch, InstallHostWorkloadInitializationStep.StepId, 50, "Preparing download"),
            CancellationToken.None);
        await Task.Delay(500);
        await renderer.OnEventAsync(
            new StartInitializationStepCompletedEvent(DateTimeOffset.UnixEpoch, InstallHostWorkloadInitializationStep.StepId, "Installed host 4.834.0"),
            CancellationToken.None);
        var result = new StartInitializationResult(
            new DashboardRunInfo(CliVersion: "5.0.0-test", ProfileName: "none", StackName: ".NET"),
            new InMemoryHostEventStream(),
            HostVersion: "4.834.0",
            BundleRequired: false,
            BundleVersion: null);
        await renderer.OnEventAsync(new StartInitializationCompletedEvent(DateTimeOffset.UnixEpoch, result), CancellationToken.None);
        await renderer.DisposeAsync();

        string output = writer.ToString();
        string normalizedOutput = output.Replace("\r\n", "\n", StringComparison.Ordinal);
        string completedIcon = console.Profile.Capabilities.Unicode ? "\u2713" : "[x]";

        Assert.Contains("Azure Functions CLI", output);
        Assert.Contains("5.0.0-test", output);
        Assert.Contains("Azure Functions CLI\n5.0.0-test\n\n", normalizedOutput);
        Assert.Contains(completedIcon, output);
        Assert.Contains("Resolve profile...", output);
        Assert.Contains("Install host workload", output);
        Assert.Contains(" 50%", output);
        Assert.Contains("\u001b[5A\u001b[J", output);
        Assert.DoesNotContain("\u001b[2J", output);
        Assert.DoesNotContain("Preparing download", output);
    }

    [Fact]
    public async Task CompactRenderer_DoesNotClearWhenInitializationStarts()
    {
        using var writer = new StringWriter();
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.Yes,
            Out = new TestTerminalOutput(writer),
        });
        var renderer = new CompactStartInitializationRenderer(new TestInteractionService(), "5.0.0-test", console);

        try
        {
            await renderer.OnEventAsync(
                new StartInitializationStepStartedEvent(
                    DateTimeOffset.UnixEpoch,
                    new StartInitializationStep(ResolveProfileInitializationStep.StepId, "Resolve profile")),
                CancellationToken.None);

            Assert.DoesNotContain("\u001b[2J", writer.ToString());
        }
        finally
        {
            await renderer.DisposeAsync();
        }
    }

    private static StartInitializationContext CreateContext(
        WorkingDirectory workingDirectory,
        string cliVersion,
        int demoFunctionCount,
        double demoSpeedMultiplier,
        bool demoAutoExit)
    {
        var options = new StartCommandOptions(
            workingDirectory,
            Port: null,
            Cors: [],
            CorsCredentials: false,
            Functions: [],
            NoBuild: false,
            EnableAuth: false,
            RequestedHostVersion: null,
            OutputMode.Compact,
            NoTui: false,
            LogFilePath: null,
            demoFunctionCount,
            demoSpeedMultiplier,
            demoAutoExit);

        return new StartInitializationContext(
            options,
            cliVersion,
            IsInteractive: true,
            CanPrompt: true);
    }

    private static IFunctionsProject CreateProject(
        WorkingDirectory workingDirectory,
        string stackName = "dotnet-isolated",
        string stackDisplayName = ".NET",
        bool supportsExtensionBundles = false)
    {
        IFunctionsWorker worker = Substitute.For<IFunctionsWorker>();
        worker.Id.Returns(new FunctionsWorkerId("dotnet"));
        worker.WorkerRuntime.Returns("dotnet-isolated");
        worker.WorkerConfigPath.Returns("c:\\some\\path");
        worker.Version.Returns("1.0.0");

        IFunctionsProject project = Substitute.For<IFunctionsProject>();
        project.WorkingDirectory.Returns(workingDirectory);
        project.StackName.Returns(stackName);
        project.StackDisplayName.Returns(stackDisplayName);
        project.SupportsExtensionBundles.Returns(supportsExtensionBundles);
        project.Worker.Returns(worker);
        return project;
    }

    private static int FindStartedStepIndex(IReadOnlyList<StartInitializationEvent> events, string stepId)
        => FindStepIndex(events, stepId, static ev => ev is StartInitializationStepStartedEvent);

    private static int FindCompletedStepIndex(IReadOnlyList<StartInitializationEvent> events, string stepId)
        => FindStepIndex(events, stepId, static ev => ev is StartInitializationStepCompletedEvent);

    private static int FindStepIndex(
        IReadOnlyList<StartInitializationEvent> events,
        string stepId,
        Func<StartInitializationEvent, bool> predicate)
    {
        for (int i = 0; i < events.Count; i++)
        {
            StartInitializationEvent initializationEvent = events[i];
            string? currentStepId = initializationEvent switch
            {
                StartInitializationStepStartedEvent started => started.Step.Id,
                StartInitializationStepCompletedEvent completed => completed.StepId,
                _ => null,
            };

            if (string.Equals(currentStepId, stepId, StringComparison.Ordinal) && predicate(initializationEvent))
            {
                return i;
            }
        }

        return -1;
    }

    private sealed class RecordingStartInitializationRenderer : IStartInitializationRenderer
    {
        public List<StartInitializationEvent> Events { get; } = [];

        public Task OnEventAsync(StartInitializationEvent initializationEvent, CancellationToken cancellationToken)
        {
            Events.Add(initializationEvent);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestTerminalOutput(TextWriter writer) : IAnsiConsoleOutput
    {
        public TextWriter Writer { get; } = writer;

        public bool IsTerminal => true;

        public int Width => 120;

        public int Height => 40;

        public void SetEncoding(Encoding encoding)
        {
        }
    }
}
