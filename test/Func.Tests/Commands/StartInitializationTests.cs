// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Commands.Start.Initialization.Rendering;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Demo;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NuGet.Versioning;
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
        FunctionsProject project = CreateProject(
            WorkingDirectory.FromExplicit(_tempDir),
            stackName: "dotnet-isolated",
            stackDisplayName: ".NET",
            supportsExtensionBundles: false);
        projectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Resolved(project, "found .csproj"));

        var renderer = new RecordingStartInitializationRenderer();
        var runner = CreateRunner(projectResolver);
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
        FunctionsProject project = CreateProject(WorkingDirectory.FromExplicit(_tempDir));
        projectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Resolved(project, "found .csproj"));
        var renderer = new RecordingStartInitializationRenderer();
        var runner = CreateRunner(projectResolver);
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
    public async Task DemoRunner_PreparesProjectHostRunBeforeStartingHost()
    {
        IFunctionsProjectResolver projectResolver = Substitute.For<IFunctionsProjectResolver>();
        TestFunctionsProject project = CreateProject(WorkingDirectory.FromExplicit(_tempDir));
        DirectoryInfo startupDirectory = new(Path.Combine(_tempDir, "bin"));
        project.PrepareAction = context => context.StartupDirectory = startupDirectory;
        projectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Resolved(project, "found .csproj"));
        var renderer = new RecordingStartInitializationRenderer();
        var runner = CreateRunner(projectResolver);
        StartInitializationContext context = CreateContext(
            WorkingDirectory.FromExplicit(_tempDir),
            cliVersion: "5.0.0-test",
            demoFunctionCount: 12,
            demoSpeedMultiplier: 0.001,
            demoAutoExit: true);

        StartInitializationResult result = await runner.RunAsync(context, renderer, CancellationToken.None);

        int prepareStartedIndex = FindStartedStepIndex(renderer.Events, PrepareProjectHostRunInitializationStep.StepId);
        int startHostStartedIndex = FindStartedStepIndex(renderer.Events, StartHostInitializationStep.StepId);

        Assert.True(prepareStartedIndex >= 0);
        Assert.True(startHostStartedIndex > prepareStartedIndex);
        Assert.Same(project, result.Project);
        Assert.Same(result.HostRunContext, project.PreparedContexts.Single());
        Assert.Equal(startupDirectory.FullName, result.HostRunContext.StartupDirectory.FullName);
        Assert.Equal("dotnet-isolated", result.HostRunContext.WorkerRuntime);
    }

    [Fact]
    public async Task DemoRunner_ResolvedProfileFlowsToHostProjectAndRunInfo()
    {
        IFunctionsProjectResolver projectResolver = Substitute.For<IFunctionsProjectResolver>();
        TestFunctionsProject project = CreateProject(
            WorkingDirectory.FromExplicit(_tempDir),
            stackName: "node",
            stackDisplayName: "Node.js",
            supportsExtensionBundles: false);
        projectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Resolved(project, "found package.json"));
        var hostRange = VersionRange.Parse("[1.8.1, 4.1048.200)");
        Dictionary<string, VersionRange> workerRanges = new(StringComparer.OrdinalIgnoreCase)
        {
            ["node"] = VersionRange.Parse("[3.13.0]"),
        };
        var profileSource = new ProfileSourceInfo(ProfileSourceKind.BuiltIn, "bundled");
        var profile = new ResolvedProfile(
            "flex",
            profileSource,
            Sku: "flex",
            ProfileStatus.Stable,
            DeprecationUrl: null,
            hostRange,
            workerRanges,
            ExtensionBundleVersionRange: VersionRange.Parse("[3.0.0, 5.0.0)"),
            SupportedRuntimes: ["node"],
            Notes: null);
        var profileResolution = new ProfileResolution.Resolved(profile, []);
        ContentWorkloadInfo hostWorkload = CreateHostWorkload("4.1000.0");
        HostWorkloadResolution hostResolution = new HostWorkloadResolution.Installed(
            hostWorkload,
            NuGetVersion.Parse("4.1000.0"),
            ExplicitlyRequested: false);
        IHostWorkloadResolver hostWorkloadResolver = Substitute.For<IHostWorkloadResolver>();
        hostWorkloadResolver.ResolveAsync(Arg.Any<HostWorkloadResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(hostResolution);
        var renderer = new RecordingStartInitializationRenderer();
        var runner = CreateRunner(projectResolver, profileResolution, hostWorkloadResolver);
        StartInitializationContext context = CreateContext(
            WorkingDirectory.FromExplicit(_tempDir),
            cliVersion: "5.0.0-test",
            demoFunctionCount: 12,
            demoSpeedMultiplier: 0.001,
            demoAutoExit: true);

        StartInitializationResult result = await runner.RunAsync(context, renderer, CancellationToken.None);

        Assert.Equal("flex", result.RunInfo.ProfileName);
        Assert.Equal("4.1000.0", result.HostVersion);
        await hostWorkloadResolver.Received(1).ResolveAsync(
            Arg.Is<HostWorkloadResolutionContext>(hostContext =>
                ReferenceEquals(hostContext.ProfileHostVersionRange, hostRange)),
            Arg.Any<CancellationToken>());
        await projectResolver.Received(1).ResolveProjectAsync(
            Arg.Is<ProjectResolutionContext>(projectContext => HasWorkerRange(projectContext, workerRanges["node"])),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DemoRunner_ProfileRejectsUnsupportedDetectedRuntime()
    {
        IFunctionsProjectResolver projectResolver = Substitute.For<IFunctionsProjectResolver>();
        TestFunctionsProject project = CreateProject(
            WorkingDirectory.FromExplicit(_tempDir),
            stackName: "node",
            stackDisplayName: "Node.js");
        projectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Resolved(project, "found package.json"));
        var profileSource = new ProfileSourceInfo(ProfileSourceKind.BuiltIn, "bundled");
        Dictionary<string, VersionRange> workerRanges = new(StringComparer.OrdinalIgnoreCase);
        var profile = new ResolvedProfile(
            "flex",
            profileSource,
            Sku: "flex",
            ProfileStatus.Stable,
            DeprecationUrl: null,
            VersionRange.Parse("[1.8.1, 4.1048.200)"),
            workerRanges,
            ExtensionBundleVersionRange: null,
            SupportedRuntimes: ["python"],
            Notes: null);
        HostWorkloadResolution hostResolution = new HostWorkloadResolution.Installed(
            CreateHostWorkload("4.1000.0"),
            NuGetVersion.Parse("4.1000.0"),
            ExplicitlyRequested: false);
        IHostWorkloadResolver hostWorkloadResolver = Substitute.For<IHostWorkloadResolver>();
        hostWorkloadResolver.ResolveAsync(Arg.Any<HostWorkloadResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(hostResolution);
        var runner = CreateRunner(projectResolver, new ProfileResolution.Resolved(profile, []), hostWorkloadResolver);
        StartInitializationContext context = CreateContext(
            WorkingDirectory.FromExplicit(_tempDir),
            cliVersion: "5.0.0-test",
            demoFunctionCount: 12,
            demoSpeedMultiplier: 0.001,
            demoAutoExit: true);

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => runner.RunAsync(context, new RecordingStartInitializationRenderer(), CancellationToken.None));

        Assert.Contains("does not support the detected runtime 'node'", ex.Message);
    }

    [Fact]
    public async Task JsonRenderer_EmitsInitializationRecords()
    {
        using var stream = new MemoryStream();
        var renderer = new JsonStartInitializationRenderer(stream, ownsStream: false);
        const string customStepId = "custom_step";
        var startedEvent = new StartInitializationStartedEvent(DateTimeOffset.UnixEpoch, "none");
        var progressEvent = new StartInitializationProgressEvent(DateTimeOffset.UnixEpoch, customStepId, 50, "Downloading");

        await renderer.OnEventAsync(startedEvent, CancellationToken.None);
        await renderer.OnEventAsync(progressEvent, CancellationToken.None);
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
    public async Task JsonRenderer_EmitsProfileDetailsOnCompletion()
    {
        using var stream = new MemoryStream();
        var renderer = new JsonStartInitializationRenderer(stream, ownsStream: false);
        Dictionary<string, string> workerVersionRanges = new(StringComparer.OrdinalIgnoreCase)
        {
            ["node"] = "[3.13.0]",
        };
        var diagnostic = new StartInitializationProfileDiagnostic("warning", "Profile warning");
        var profile = new StartInitializationProfileInfo(
            "flex",
            "builtin",
            "bundled",
            "[1.8.1, 4.1048.200)",
            workerVersionRanges,
            "[3.0.0, 5.0.0)",
            ["node"],
            [diagnostic]);
        var runInfo = new DashboardRunInfo(CliVersion: "5.0.0-test", ProfileName: "flex", StackName: "Node.js");
        var eventStream = new InMemoryHostEventStream();
        FunctionsProject project = CreateProject(
            WorkingDirectory.FromExplicit(Environment.CurrentDirectory),
            stackName: "node",
            stackDisplayName: "Node.js");
        FunctionsProjectHostRunContext hostRunContext = CreateHostRunContext(WorkingDirectory.FromExplicit(Environment.CurrentDirectory));
        var result = new StartInitializationResult(
            runInfo,
            eventStream,
            HostVersion: "4.1000.0",
            BundleRequired: true,
            BundleVersion: "4.35.0",
            project,
            hostRunContext,
            profile);
        var completedEvent = new StartInitializationCompletedEvent(DateTimeOffset.UnixEpoch, result);

        await renderer.OnEventAsync(completedEvent, CancellationToken.None);
        await renderer.DisposeAsync();

        string line = Encoding.UTF8.GetString(stream.ToArray()).Trim();
        using var document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        JsonElement profileDetails = root.GetProperty("profile_details");

        Assert.Equal("start_initialization_completed", root.GetProperty("kind").GetString());
        Assert.Equal("node", root.GetProperty("worker_runtime").GetString());
        Assert.Equal("flex", profileDetails.GetProperty("name").GetString());
        Assert.Equal("[1.8.1, 4.1048.200)", profileDetails.GetProperty("host_version_range").GetString());
        Assert.Equal("[3.13.0]", profileDetails.GetProperty("worker_version_ranges").GetProperty("node").GetString());
        Assert.Equal("warning", profileDetails.GetProperty("diagnostics")[0].GetProperty("severity").GetString());
    }

    [Fact]
    public async Task CompactRenderer_RendersChecklistLines()
    {
        using var writer = new StringWriter();
        IAnsiConsole console = CreateInteractiveConsole(writer);
        var renderer = new CompactStartInitializationRenderer(new TestInteractionService(), "5.0.0-test", console);
        var startedEvent = new StartInitializationStartedEvent(DateTimeOffset.UnixEpoch, "none");
        var resolveProfileStep = new StartInitializationStep(ResolveProfileInitializationStep.StepId, "Resolve profile");
        var resolveStartedEvent = new StartInitializationStepStartedEvent(DateTimeOffset.UnixEpoch, resolveProfileStep);
        var resolveCompletedEvent = new StartInitializationStepCompletedEvent(
            DateTimeOffset.UnixEpoch,
            ResolveProfileInitializationStep.StepId,
            "none");
        var installStep = new StartInitializationStep(
            InstallHostWorkloadInitializationStep.StepId,
            "Install host workload",
            DisplayKind: StartInitializationDisplayKind.Progress);
        var installStartedEvent = new StartInitializationStepStartedEvent(DateTimeOffset.UnixEpoch, installStep);
        var installProgressEvent = new StartInitializationProgressEvent(
            DateTimeOffset.UnixEpoch,
            InstallHostWorkloadInitializationStep.StepId,
            50,
            "Preparing download");
        var installCompletedEvent = new StartInitializationStepCompletedEvent(
            DateTimeOffset.UnixEpoch,
            InstallHostWorkloadInitializationStep.StepId,
            "Installed host 4.834.0");

        await renderer.OnEventAsync(startedEvent, CancellationToken.None);
        await renderer.OnEventAsync(resolveStartedEvent, CancellationToken.None);
        await Task.Delay(500);
        await renderer.OnEventAsync(resolveCompletedEvent, CancellationToken.None);
        await renderer.OnEventAsync(installStartedEvent, CancellationToken.None);
        await renderer.OnEventAsync(installProgressEvent, CancellationToken.None);
        await Task.Delay(500);
        await renderer.OnEventAsync(installCompletedEvent, CancellationToken.None);
        var runInfo = new DashboardRunInfo(CliVersion: "5.0.0-test", ProfileName: "none", StackName: ".NET");
        var eventStream = new InMemoryHostEventStream();
        FunctionsProject project = CreateProject(WorkingDirectory.FromExplicit(Environment.CurrentDirectory));
        FunctionsProjectHostRunContext hostRunContext = CreateHostRunContext(WorkingDirectory.FromExplicit(Environment.CurrentDirectory));
        var result = new StartInitializationResult(
            runInfo,
            eventStream,
            HostVersion: "4.834.0",
            BundleRequired: false,
            BundleVersion: null,
            project,
            hostRunContext);
        var completedEvent = new StartInitializationCompletedEvent(DateTimeOffset.UnixEpoch, result);

        await renderer.OnEventAsync(completedEvent, CancellationToken.None);
        await renderer.DisposeAsync();

        string output = writer.ToString();
        string completedIcon = console.Profile.Capabilities.Unicode ? "\u2713" : "[x]";

        Assert.Contains("Azure Functions CLI", output);
        Assert.Contains("5.0.0-test", output);
        Assert.Contains(completedIcon, output);
        Assert.Contains("Resolve profile...", output);
        Assert.Contains("Install host workload", output);
        Assert.Contains(" 50%", output);
        Assert.DoesNotContain("\u001b[2J", output);
        Assert.DoesNotContain("Preparing download", output);
    }

    [Fact]
    public async Task CompactRenderer_DoesNotClearWhenInitializationStarts()
    {
        using var writer = new StringWriter();
        IAnsiConsole console = CreateInteractiveConsole(writer);
        var renderer = new CompactStartInitializationRenderer(new TestInteractionService(), "5.0.0-test", console);

        try
        {
            var step = new StartInitializationStep(ResolveProfileInitializationStep.StepId, "Resolve profile");
            var startedEvent = new StartInitializationStepStartedEvent(DateTimeOffset.UnixEpoch, step);

            await renderer.OnEventAsync(startedEvent, CancellationToken.None);

            Assert.DoesNotContain("\u001b[2J", writer.ToString());
        }
        finally
        {
            await renderer.DisposeAsync();
        }
    }

    [Fact]
    public async Task CompactRenderer_AnimatesStatusStepBetweenEvents()
    {
        using var writer = new StringWriter();
        IAnsiConsole console = CreateInteractiveConsole(writer);
        var renderer = new CompactStartInitializationRenderer(new TestInteractionService(), "5.0.0-test", console);

        try
        {
            var initializationStartedEvent = new StartInitializationStartedEvent(DateTimeOffset.UnixEpoch, "none");
            var step = new StartInitializationStep(ResolveProfileInitializationStep.StepId, "Resolve profile");
            var stepStartedEvent = new StartInitializationStepStartedEvent(DateTimeOffset.UnixEpoch, step);

            await renderer.OnEventAsync(initializationStartedEvent, CancellationToken.None);
            await renderer.OnEventAsync(stepStartedEvent, CancellationToken.None);

            Spinner spinner = console.Profile.Capabilities.Unicode ? Spinner.Known.Dots : Spinner.Known.Line;
            string output = string.Empty;
            int renderedFrameCount = 0;
            for (int attempt = 0; attempt < 20 && renderedFrameCount < 2; attempt++)
            {
                await Task.Delay(100);
                output = writer.ToString();
                renderedFrameCount = spinner.Frames.Distinct().Count(frame => output.Contains(frame, StringComparison.Ordinal));
            }

            Assert.True(renderedFrameCount >= 2, $"Expected multiple spinner frames, but saw {renderedFrameCount}. Output: {output}");
            Assert.DoesNotContain("\r\u001b[2K", output);
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
            RequestedProfileName: null,
            RequestedHostVersion: null,
            Offline: false,
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

    private static TestFunctionsProject CreateProject(
        WorkingDirectory workingDirectory,
        string stackName = "dotnet-isolated",
        string stackDisplayName = ".NET",
        bool supportsExtensionBundles = false)
    {
        IFunctionsWorker worker = Substitute.For<IFunctionsWorker>();
        worker.Id.Returns(new FunctionsWorkerId(stackName));
        worker.WorkerRuntime.Returns(stackName);
        worker.WorkerConfigPath.Returns("c:\\some\\path");
        worker.Version.Returns("1.0.0");

        return new TestFunctionsProject(
            workingDirectory,
            stackName,
            stackDisplayName,
            supportsExtensionBundles,
            worker);
    }

    private static FunctionsProjectHostRunContext CreateHostRunContext(WorkingDirectory workingDirectory)
        => new(
            workingDirectory.Info,
            "dotnet-isolated",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    private static IAnsiConsole CreateInteractiveConsole(TextWriter writer)
        => AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.Yes,
            Out = new TestTerminalOutput(writer),
        });

    private static DemoStartInitializationRunner CreateRunner(
        IFunctionsProjectResolver projectResolver,
        ProfileResolution? profileResolution = null,
        IHostWorkloadResolver? hostWorkloadResolver = null,
        IWorkloadInstaller? workloadInstaller = null)
    {
        IProfileResolver profileResolver = Substitute.For<IProfileResolver>();
        profileResolver.ResolveAsync(Arg.Any<ProfileResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => profileResolution ?? new ProfileResolution.None([]));

        if (hostWorkloadResolver is null)
        {
            hostWorkloadResolver = Substitute.For<IHostWorkloadResolver>();
            HostWorkloadResolution defaultHostResolution =
                new HostWorkloadResolution.InstallRequired("4.834.0", "No installed host workload found for 4.834.0");
            hostWorkloadResolver.ResolveAsync(Arg.Any<HostWorkloadResolutionContext>(), Arg.Any<CancellationToken>())
                .Returns(defaultHostResolution);
        }

        workloadInstaller ??= CreateSuccessfulHostInstaller();
        IExtensionBundleResolver bundleResolver = Substitute.For<IExtensionBundleResolver>();
        var bundleSectionReader = new HostJsonBundleSectionReader();

        return new DemoStartInitializationRunner(
            projectResolver,
            bundleResolver,
            bundleSectionReader,
            profileResolver,
            hostWorkloadResolver,
            workloadInstaller,
            NullLoggerFactory.Instance);
    }

    private static IWorkloadInstaller CreateSuccessfulHostInstaller()
    {
        IWorkloadInstaller workloadInstaller = Substitute.For<IWorkloadInstaller>();
        workloadInstaller.InstallFromCatalogAsync(
                Arg.Any<string>(),
                Arg.Any<NuGetVersion?>(),
                Arg.Any<string?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IProgress<WorkloadInstallProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var version = (NuGetVersion?)callInfo[1];
                string packageVersion = version?.ToNormalizedString() ?? "4.834.0";
                return new WorkloadInstallResult(CreateHostEntry(packageVersion), AlreadyInstalled: false);
            });

        return workloadInstaller;
    }

    private static WorkloadEntry CreateHostEntry(string packageVersion)
        => new()
        {
            PackageId = HostWorkloadPackage.CurrentPackageId,
            PackageVersion = packageVersion,
            Kind = WorkloadKind.Content,
            Aliases = ["host"],
            DisplayName = "Azure Functions host",
            Description = string.Empty,
        };

    private static ContentWorkloadInfo CreateHostWorkload(string packageVersion)
        => new(
            PackageId: HostWorkloadPackage.CurrentPackageId,
            PackageVersion: packageVersion,
            Aliases: ["host"],
            InstallDirectory: Path.Combine(Path.GetTempPath(), "workloads", HostWorkloadPackage.CurrentPackageId, packageVersion),
            ContentRoot: Path.Combine(Path.GetTempPath(), "workloads", HostWorkloadPackage.CurrentPackageId, packageVersion, "tools", "any"),
            DisplayName: "Azure Functions host",
            Description: string.Empty);

    private static bool HasWorkerRange(ProjectResolutionContext context, VersionRange expectedRange)
        => context.WorkerVersionRanges.TryGetValue("node", out VersionRange? actualRange)
           && ReferenceEquals(actualRange, expectedRange);

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

    private sealed class TestFunctionsProject(
        WorkingDirectory workingDirectory,
        string stackName,
        string stackDisplayName,
        bool supportsExtensionBundles,
        IFunctionsWorker worker) : FunctionsProject
    {
        private readonly WorkingDirectory _workingDirectory = workingDirectory;
        private readonly IFunctionsWorker _worker = worker;

        public List<FunctionsProjectHostRunContext> PreparedContexts { get; } = [];

        public Action<FunctionsProjectHostRunContext>? PrepareAction { get; set; }

        public override WorkingDirectory WorkingDirectory => _workingDirectory;

        public override string StackName => stackName;

        public override string StackDisplayName => stackDisplayName;

        public override bool SupportsExtensionBundles => supportsExtensionBundles;

        public override IFunctionsWorker Worker => _worker;

        public override Task PrepareForHostRunAsync(
            FunctionsProjectHostRunContext context,
            CancellationToken cancellationToken)
        {
            PrepareAction?.Invoke(context);
            PreparedContexts.Add(context);
            return Task.CompletedTask;
        }
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
