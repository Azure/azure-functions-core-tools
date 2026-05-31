// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Start.Azurite.Orchestration;
using Azure.Functions.Cli.Commands.Start.Host;
using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Commands.Start.Initialization.Rendering;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
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
    public async Task DemoRunner_PopulatesEnvironmentVariablesForHostRun()
    {
        File.WriteAllText(
            Path.Combine(_tempDir, "local.settings.json"),
            """{ "Values": { "FROM_LOCAL": "yes", "FUNCTIONS_WORKER_RUNTIME": "stale" } }""");

        string workerDir = Path.Combine(_tempDir, "workers", "node");
        Directory.CreateDirectory(workerDir);
        string workerConfigPath = Path.Combine(workerDir, "worker.config.json");

        IFunctionsProjectResolver projectResolver = Substitute.For<IFunctionsProjectResolver>();
        TestFunctionsProject project = CreateProject(
            WorkingDirectory.FromExplicit(_tempDir),
            stackName: "node",
            stackDisplayName: "Node.js");
        projectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Resolved(project, "found package.json"));

        IFunctionsWorkerResolver workerResolver = Substitute.For<IFunctionsWorkerResolver>();
        workerResolver.ResolveWorkerAsync(Arg.Any<FunctionsWorkerId>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                FunctionsWorkerId id = callInfo.ArgAt<FunctionsWorkerId>(0);
                IFunctionsWorker worker = new TestFunctionsWorker(id, id.Value, workerConfigPath, "1.0.0");
                return FunctionsWorkerResolutionResults.Resolved(worker);
            });
        IFunctionsWorkerResolverFactory workerResolverFactory = CreateWorkerResolverFactory(workerResolver);

        var renderer = new RecordingStartInitializationRenderer();
        var runner = CreateRunner(projectResolver, workerResolverFactory: workerResolverFactory);
        StartInitializationContext context = CreateContext(
            WorkingDirectory.FromExplicit(_tempDir),
            cliVersion: "5.0.0-test",
            demoFunctionCount: 1,
            demoSpeedMultiplier: 0.001,
            demoAutoExit: true);

        StartInitializationResult result = await runner.RunAsync(context, renderer, CancellationToken.None);

        IDictionary<string, string> env = result.HostRunContext.EnvironmentVariables;
        Assert.Equal("yes", env["FROM_LOCAL"]);
        Assert.Equal("node", env["FUNCTIONS_WORKER_RUNTIME"]);
        Assert.Equal(workerDir, env["languageWorkers__node__workerDirectory"]);
    }

    [Fact]
    public async Task Runner_RealMode_StartsHostProcessRunnerWithPreparedContext()
    {
        IFunctionsProjectResolver projectResolver = Substitute.For<IFunctionsProjectResolver>();
        TestFunctionsProject project = CreateProject(WorkingDirectory.FromExplicit(_tempDir));
        DirectoryInfo startupDirectory = new(Path.Combine(_tempDir, "bin"));
        project.PrepareAction = context =>
        {
            context.StartupDirectory = startupDirectory;
            context.EnvironmentVariables["CUSTOM_ENV"] = "custom";
        };
        projectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Resolved(project, "found .csproj"));
        ContentWorkloadInfo hostWorkload = CreateHostWorkload("4.1000.0");
        IHostWorkloadResolver hostWorkloadResolver = Substitute.For<IHostWorkloadResolver>();
        hostWorkloadResolver.ResolveAsync(Arg.Any<HostWorkloadResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(new HostWorkloadResolution.Installed(
                hostWorkload,
                NuGetVersion.Parse("4.1000.0"),
                ExplicitlyRequested: false));
        IHostProcessRunner hostProcessRunner = Substitute.For<IHostProcessRunner>();
        var eventStream = new InMemoryHostEventStream();
        eventStream.Complete();
        hostProcessRunner.StartAsync(Arg.Any<HostProcessStartContext>(), Arg.Any<CancellationToken>())
            .Returns(eventStream);
        var renderer = new RecordingStartInitializationRenderer();
        var runner = CreateRunner(projectResolver, hostWorkloadResolver: hostWorkloadResolver, hostProcessRunner: hostProcessRunner);
        StartInitializationContext context = CreateContext(
            WorkingDirectory.FromExplicit(_tempDir),
            cliVersion: "5.0.0-test",
            demoFunctionCount: 12,
            demoSpeedMultiplier: 0.001,
            demoAutoExit: true,
            demoMode: false);

        StartInitializationResult result = await runner.RunAsync(context, renderer, CancellationToken.None);

        Assert.Same(eventStream, result.EventStream);
        await hostProcessRunner.Received(1).StartAsync(
            Arg.Is<HostProcessStartContext>(startContext =>
                ReferenceEquals(startContext.HostWorkload, hostWorkload)
                && ReferenceEquals(startContext.HostRunContext, result.HostRunContext)
                && startContext.HostRunContext.StartupDirectory.FullName == startupDirectory.FullName
                && startContext.HostRunContext.EnvironmentVariables["CUSTOM_ENV"] == "custom"
                && !startContext.Options.DemoMode),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Runner_HostContentRootOverride_BypassesResolutionAndInstall()
    {
        string contentRoot = CreateLocalHostContentRoot();
        using var environment = new EnvironmentVariableScope(
            ValidateHostWorkloadInitializationStep.HostContentRootEnvironmentVariable,
            contentRoot);
        IFunctionsProjectResolver projectResolver = Substitute.For<IFunctionsProjectResolver>();
        TestFunctionsProject project = CreateProject(WorkingDirectory.FromExplicit(_tempDir));
        projectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Resolved(project, "found .csproj"));
        IHostWorkloadResolver hostWorkloadResolver = Substitute.For<IHostWorkloadResolver>();
        hostWorkloadResolver.ResolveAsync(Arg.Any<HostWorkloadResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HostWorkloadResolution>(
                new InvalidOperationException("Host workload resolution should be bypassed.")));
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
            .Returns(Task.FromException<WorkloadInstallResult>(
                new InvalidOperationException("Host workload installation should be bypassed.")));
        HostProcessStartContext? capturedStartContext = null;
        IHostProcessRunner hostProcessRunner = Substitute.For<IHostProcessRunner>();
        var eventStream = new InMemoryHostEventStream();
        eventStream.Complete();
        hostProcessRunner.StartAsync(Arg.Do<HostProcessStartContext>(value => capturedStartContext = value), Arg.Any<CancellationToken>())
            .Returns(eventStream);
        var renderer = new RecordingStartInitializationRenderer();
        var runner = CreateRunner(
            projectResolver,
            hostWorkloadResolver: hostWorkloadResolver,
            workloadInstaller: workloadInstaller,
            hostProcessRunner: hostProcessRunner);
        StartInitializationContext context = CreateContext(
            WorkingDirectory.FromExplicit(_tempDir),
            cliVersion: "5.0.0-test",
            demoFunctionCount: 12,
            demoSpeedMultiplier: 0.001,
            demoAutoExit: true,
            demoMode: false);

        StartInitializationResult result = await runner.RunAsync(context, renderer, CancellationToken.None);

        Assert.Equal("local-dev", result.HostVersion);
        Assert.Same(eventStream, result.EventStream);
        Assert.NotNull(capturedStartContext);
        HostProcessStartContext startContext = capturedStartContext!;
        Assert.Equal(contentRoot, startContext.HostWorkload.ContentRoot);
        Assert.Equal("Azure.Functions.Cli.Workloads.Host.local", startContext.HostWorkload.PackageId);
        Assert.Contains("host", startContext.HostWorkload.Aliases);
        Assert.Equal(-1, FindStartedStepIndex(renderer.Events, InstallHostWorkloadInitializationStep.StepId));
    }

    [Fact]
    public async Task Runner_HostContentRootOverride_RejectsMissingExecutable()
    {
        string contentRoot = Path.Combine(_tempDir, "invalid-host-content");
        Directory.CreateDirectory(contentRoot);
        using var environment = new EnvironmentVariableScope(
            ValidateHostWorkloadInitializationStep.HostContentRootEnvironmentVariable,
            contentRoot);
        IFunctionsProjectResolver projectResolver = Substitute.For<IFunctionsProjectResolver>();
        var runner = CreateRunner(projectResolver);
        StartInitializationContext context = CreateContext(
            WorkingDirectory.FromExplicit(_tempDir),
            cliVersion: "5.0.0-test",
            demoFunctionCount: 12,
            demoSpeedMultiplier: 0.001,
            demoAutoExit: true,
            demoMode: false);
        var renderer = new RecordingStartInitializationRenderer();

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => runner.RunAsync(context, renderer, CancellationToken.None));

        Assert.Contains(ValidateHostWorkloadInitializationStep.HostContentRootEnvironmentVariable, ex.Message);
        Assert.Contains("host executable was not found", ex.Message);
        Assert.Contains(renderer.Events, ev => ev is StartInitializationStepFailedEvent failed
            && failed.StepId == ValidateHostWorkloadInitializationStep.StepId
            && failed.Message is not null
            && failed.Message.Contains("host executable was not found", StringComparison.Ordinal));
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
        IFunctionsWorkerResolverFactory workerResolverFactory = CreateWorkerResolverFactory();
        var runner = CreateRunner(projectResolver, profileResolution, hostWorkloadResolver, workerResolverFactory: workerResolverFactory);
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
            Arg.Is<ProjectResolutionContext>(projectContext =>
                projectContext.WorkingDirectory.Info.FullName == _tempDir),
            Arg.Any<CancellationToken>());
        workerResolverFactory.Received(1).Create(
            Arg.Is<IReadOnlyDictionary<string, VersionRange>>(ranges =>
                HasWorkerRange(ranges, workerRanges["node"])));
    }

    [Fact]
    public async Task DemoRunner_ProfiledMissingWorkerUsesInstalledWorkerWithoutRetryingResolver()
    {
        IFunctionsProjectResolver projectResolver = Substitute.For<IFunctionsProjectResolver>();
        TestFunctionsProject project = CreateProject(
            WorkingDirectory.FromExplicit(_tempDir),
            stackName: "node",
            stackDisplayName: "Node.js");
        projectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Resolved(project, "found package.json"));
        var workerId = new FunctionsWorkerId("node");
        FunctionsWorkerResolutionFailure failure = CreateNotInstalledWorkerFailure("node");
        IFunctionsWorkerResolver workerResolver = Substitute.For<IFunctionsWorkerResolver>();
        workerResolver.ResolveWorkerAsync(workerId, Arg.Any<CancellationToken>())
            .Returns(FunctionsWorkerResolutionResults.NotResolved(failure));
        IFunctionsWorkerResolverFactory workerResolverFactory = CreateWorkerResolverFactory(workerResolver);
        var workerRange = VersionRange.Parse("[3.13.0]");
        Dictionary<string, VersionRange> workerRanges = new(StringComparer.OrdinalIgnoreCase)
        {
            ["node"] = workerRange,
        };
        IFunctionsWorker installedWorker = CreateWorker("node", "node", "3.13.0");
        IFunctionsWorkerInstaller workerInstaller = Substitute.For<IFunctionsWorkerInstaller>();
        WorkloadInstallResult workloadInstallResult = new(CreateWorkerEntry(FunctionsWorkerWorkloadPackages.GetPackageId(workerId), "3.13.0"), AlreadyInstalled: false);
        workerInstaller.InstallAsync(workerId, Arg.Is<IReadOnlyDictionary<string, VersionRange>>(ranges => HasWorkerRange(ranges, workerRange)), Arg.Any<CancellationToken>())
            .Returns(new FunctionsWorkerInstallResult(installedWorker, workloadInstallResult));
        var renderer = new RecordingStartInitializationRenderer();
        var runner = CreateRunner(
            projectResolver,
            CreateResolvedProfile(workerRanges),
            CreateInstalledHostWorkloadResolver(),
            workerInstaller: workerInstaller,
            workerResolverFactory: workerResolverFactory,
            hostProcessRunner: CreateSuccessfulHostProcessRunner());
        StartInitializationContext context = CreateContext(
            WorkingDirectory.FromExplicit(_tempDir),
            cliVersion: "5.0.0-test",
            demoFunctionCount: 12,
            demoSpeedMultiplier: 0.001,
            demoAutoExit: true);

        StartInitializationResult result = await runner.RunAsync(
            context,
            renderer,
            CancellationToken.None);

        Assert.Equal("node", result.Worker.WorkerRuntime);
        Assert.Equal("3.13.0", result.Worker.Version);
        Assert.Same(installedWorker, result.Worker);
        await workerResolver.Received(1).ResolveWorkerAsync(workerId, Arg.Any<CancellationToken>());
        await workerInstaller.Received(1)
            .InstallAsync(workerId, Arg.Is<IReadOnlyDictionary<string, VersionRange>>(ranges => HasWorkerRange(ranges, workerRange)), Arg.Any<CancellationToken>());
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
    public async Task DemoRunner_WorkerProjectCreationFailure_ThrowsWithoutInstallingWorker()
    {
        IFunctionsProjectResolver projectResolver = Substitute.For<IFunctionsProjectResolver>();
        FunctionsWorkerResolutionFailure workerFailure = CreateNotInstalledWorkerFailure("node");
        projectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(CreateWorkerNotResolvedResult(workerFailure));
        IWorkloadInstaller workloadInstaller = CreateSuccessfulInstaller();
        var runner = CreateRunner(
            projectResolver,
            hostWorkloadResolver: CreateInstalledHostWorkloadResolver(),
            workloadInstaller: workloadInstaller);
        StartInitializationContext context = CreateContext(
            WorkingDirectory.FromExplicit(_tempDir),
            cliVersion: "5.0.0-test",
            demoFunctionCount: 12,
            demoSpeedMultiplier: 0.001,
            demoAutoExit: true);

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => runner.RunAsync(context, new RecordingStartInitializationRenderer(), CancellationToken.None));

        Assert.Contains(workerFailure.Message, ex.Message);
        await projectResolver.Received(1).ResolveProjectAsync(
            Arg.Any<ProjectResolutionContext>(),
            Arg.Any<CancellationToken>());
        await workloadInstaller.DidNotReceive().InstallFromCatalogAsync(
            Arg.Any<string>(),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<string?>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<bool>(),
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>());
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
    public async Task JsonRenderer_EmitsInitializationLogAndFailureRecords()
    {
        using var stream = new MemoryStream();
        var renderer = new JsonStartInitializationRenderer(stream, ownsStream: false);
        var logEvent = new StartInitializationLogEvent(DateTimeOffset.UnixEpoch, "prepare", "npm output", FunctionsProjectReportSeverity.Warning);
        var failedEvent = new StartInitializationStepFailedEvent(DateTimeOffset.UnixEpoch, "prepare", "build failed");

        await renderer.OnEventAsync(logEvent, CancellationToken.None);
        await renderer.OnEventAsync(failedEvent, CancellationToken.None);
        await renderer.DisposeAsync();

        string[] lines = Encoding.UTF8.GetString(stream.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        using var log = JsonDocument.Parse(lines[0]);
        Assert.Equal("start_initialization_log", log.RootElement.GetProperty("kind").GetString());
        Assert.Equal("prepare", log.RootElement.GetProperty("step").GetString());
        Assert.Equal("npm output", log.RootElement.GetProperty("line").GetString());
        Assert.Equal("warning", log.RootElement.GetProperty("severity").GetString());
        using var failed = JsonDocument.Parse(lines[1]);
        Assert.Equal("start_initialization_step_failed", failed.RootElement.GetProperty("kind").GetString());
        Assert.Equal("build failed", failed.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PlainRenderer_EmitsInitializationLogRecords()
    {
        var interaction = new TestInteractionService();
        var renderer = new PlainStartInitializationRenderer(interaction);
        var logEvent = new StartInitializationLogEvent(DateTimeOffset.UnixEpoch, "prepare", "npm output", FunctionsProjectReportSeverity.Info);

        await renderer.OnEventAsync(logEvent, CancellationToken.None);

        Assert.Contains("[init] prepare  npm output", interaction.Lines);
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
        IFunctionsWorker worker = CreateWorker("node", "node");
        var result = new StartInitializationResult(
            runInfo,
            eventStream,
            HostVersion: "4.1000.0",
            BundleRequired: true,
            BundleVersion: "4.35.0",
            project,
            worker,
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
        IFunctionsWorker worker = CreateWorker("dotnet-isolated", "dotnet-isolated");
        var result = new StartInitializationResult(
            runInfo,
            eventStream,
            HostVersion: "4.834.0",
            BundleRequired: false,
            BundleVersion: null,
            project,
            worker,
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
        Assert.Contains("Preparing download", output);
    }

    [Fact]
    public async Task CompactRenderer_RendersBoundedLogTailAndCollapsesOnSuccess()
    {
        using var writer = new StringWriter();
        IAnsiConsole console = CreateInteractiveConsole(writer);
        var renderer = new CompactStartInitializationRenderer(new TestInteractionService(), "5.0.0-test", console);
        var step = new StartInitializationStep("prepare", "Prepare project", DisplayKind: StartInitializationDisplayKind.Progress);

        await renderer.OnEventAsync(new StartInitializationStartedEvent(DateTimeOffset.UnixEpoch, "none"), CancellationToken.None);
        await renderer.OnEventAsync(new StartInitializationStepStartedEvent(DateTimeOffset.UnixEpoch, step), CancellationToken.None);
        await renderer.OnEventAsync(new StartInitializationProgressEvent(DateTimeOffset.UnixEpoch, "prepare", double.NaN, "running npm install"), CancellationToken.None);
        for (int i = 1; i <= 12; i++)
        {
            await renderer.OnEventAsync(new StartInitializationLogEvent(DateTimeOffset.UnixEpoch, "prepare", $"entry {i:00}", FunctionsProjectReportSeverity.Info), CancellationToken.None);
        }

        await WaitForOutputAsync(writer, output => output.Contains("entry 12", StringComparison.Ordinal));
        string runningOutput = writer.ToString();

        Assert.Contains("running npm install", runningOutput);
        Assert.DoesNotContain("entry 01", runningOutput);
        Assert.DoesNotContain("entry 02", runningOutput);
        Assert.Contains("entry 03", runningOutput);
        Assert.Contains("entry 12", runningOutput);

        await renderer.OnEventAsync(new StartInitializationStepCompletedEvent(DateTimeOffset.UnixEpoch, "prepare", "ready"), CancellationToken.None);
        await renderer.OnEventAsync(new StartInitializationCompletedEvent(DateTimeOffset.UnixEpoch, CreateResult()), CancellationToken.None);
        await renderer.DisposeAsync();

        string completedOutput = writer.ToString();
        Assert.Contains("12 lines", completedOutput);
        Assert.Contains("ready", completedOutput);
    }

    [Fact]
    public async Task CompactRenderer_RetainsLogTailOnFailure()
    {
        using var writer = new StringWriter();
        IAnsiConsole console = CreateInteractiveConsole(writer);
        var renderer = new CompactStartInitializationRenderer(new TestInteractionService(), "5.0.0-test", console);
        var step = new StartInitializationStep("prepare", "Prepare project", DisplayKind: StartInitializationDisplayKind.Progress);

        await renderer.OnEventAsync(new StartInitializationStartedEvent(DateTimeOffset.UnixEpoch, "none"), CancellationToken.None);
        await renderer.OnEventAsync(new StartInitializationStepStartedEvent(DateTimeOffset.UnixEpoch, step), CancellationToken.None);
        await renderer.OnEventAsync(new StartInitializationLogEvent(DateTimeOffset.UnixEpoch, "prepare", "error tail", FunctionsProjectReportSeverity.Error), CancellationToken.None);
        await renderer.OnEventAsync(new StartInitializationStepFailedEvent(DateTimeOffset.UnixEpoch, "prepare", "build failed"), CancellationToken.None);
        await WaitForOutputAsync(writer, output => output.Contains("build failed", StringComparison.Ordinal)
            && output.Contains("error tail", StringComparison.Ordinal));
        await renderer.DisposeAsync();

        string output = writer.ToString();

        Assert.Contains("build failed", output);
        Assert.Contains("error tail", output);
        Assert.DoesNotContain("1 lines", output);
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

    [Fact]
    public async Task CompactRenderer_ConfirmAsync_PreservesStatusBeforePromptAndPausesLiveDisplay()
    {
        using var writer = new StringWriter();
        IAnsiConsole console = CreateInteractiveConsole(writer);
        IInteractionService interaction = Substitute.For<IInteractionService>();
        interaction.Theme.Returns(new DefaultTheme());
        TaskCompletionSource<bool> promptStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> promptResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
        interaction.ConfirmAsync("Install?", true, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                writer.WriteLine("Install?");
                promptStarted.TrySetResult(true);
                return promptResult.Task;
            });
        var renderer = new CompactStartInitializationRenderer(interaction, "5.0.0-test", console);

        try
        {
            var initializationStartedEvent = new StartInitializationStartedEvent(DateTimeOffset.UnixEpoch, "none");
            var step = new StartInitializationStep(
                ResolveFunctionsWorkerInitializationStep.StepId,
                "Resolve worker",
                DisplayKind: StartInitializationDisplayKind.Progress);
            var stepStartedEvent = new StartInitializationStepStartedEvent(DateTimeOffset.UnixEpoch, step);

            await renderer.OnEventAsync(initializationStartedEvent, CancellationToken.None);
            await renderer.OnEventAsync(stepStartedEvent, CancellationToken.None);
            await WaitForOutputAsync(writer, output => output.Contains("Resolve worker", StringComparison.Ordinal));

            Task<bool> confirmTask = renderer.ConfirmAsync("Install?", defaultValue: true, CancellationToken.None);
            await promptStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            string outputDuringPrompt = writer.ToString();
            int statusIndex = outputDuringPrompt.LastIndexOf("Resolve worker", StringComparison.Ordinal);
            int promptIndex = outputDuringPrompt.LastIndexOf("Install?", StringComparison.Ordinal);

            await Task.Delay(300);

            Assert.True(statusIndex >= 0, $"Expected status output before prompt. Output: {outputDuringPrompt}");
            Assert.True(promptIndex > statusIndex, $"Expected prompt to render after status output. Output: {outputDuringPrompt}");
            Assert.DoesNotContain("\r\u001b[2K", outputDuringPrompt[statusIndex..promptIndex]);
            Assert.Equal(outputDuringPrompt, writer.ToString());

            promptResult.SetResult(true);
            Assert.True(await confirmTask);
            await WaitForOutputAsync(writer, output => output.Length > outputDuringPrompt.Length);
        }
        finally
        {
            promptResult.TrySetResult(true);
            await renderer.DisposeAsync();
        }
    }

    private static StartInitializationContext CreateContext(
        WorkingDirectory workingDirectory,
        string cliVersion,
        int demoFunctionCount,
        double demoSpeedMultiplier,
        bool demoAutoExit,
        bool offline = false,
        bool canPrompt = true,
        bool demoMode = true)
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
            Offline: offline,
            OutputMode.Compact,
            NoTui: false,
            LogFilePath: null,
            DemoMode: demoMode,
            demoFunctionCount,
            demoSpeedMultiplier,
            demoAutoExit);

        return new StartInitializationContext(
            options,
            cliVersion,
            IsInteractive: canPrompt,
            CanPrompt: canPrompt);
    }

    private static TestFunctionsProject CreateProject(
        WorkingDirectory workingDirectory,
        string stackName = "dotnet-isolated",
        string stackDisplayName = ".NET",
        bool supportsExtensionBundles = false)
        => new(workingDirectory, stackName, stackDisplayName, supportsExtensionBundles);

    private static FunctionsProjectHostRunContext CreateHostRunContext(WorkingDirectory workingDirectory)
        => new(
            workingDirectory.Info,
            "dotnet-isolated",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["FUNCTIONS_WORKER_RUNTIME"] = "dotnet-isolated",
            });

    private static StartInitializationResult CreateResult()
    {
        var runInfo = new DashboardRunInfo(CliVersion: "5.0.0-test", ProfileName: "none", StackName: ".NET");
        var eventStream = new InMemoryHostEventStream();
        FunctionsProject project = CreateProject(WorkingDirectory.FromExplicit(Environment.CurrentDirectory));
        FunctionsProjectHostRunContext hostRunContext = CreateHostRunContext(WorkingDirectory.FromExplicit(Environment.CurrentDirectory));
        IFunctionsWorker worker = CreateWorker("dotnet-isolated", "dotnet-isolated");
        return new StartInitializationResult(
            runInfo,
            eventStream,
            HostVersion: "4.834.0",
            BundleRequired: false,
            BundleVersion: null,
            project,
            worker,
            hostRunContext);
    }

    private static IAnsiConsole CreateInteractiveConsole(TextWriter writer)
        => AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.Yes,
            Out = new TestTerminalOutput(writer),
        });

    private static async Task WaitForOutputAsync(StringWriter writer, Func<string, bool> predicate)
    {
        string output = string.Empty;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            output = writer.ToString();
            if (predicate(output))
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.True(predicate(output), $"Expected output condition was not met. Output: {output}");
    }

    private static DemoStartInitializationRunner CreateRunner(
        IFunctionsProjectResolver projectResolver,
        ProfileResolution? profileResolution = null,
        IHostWorkloadResolver? hostWorkloadResolver = null,
        IWorkloadInstaller? workloadInstaller = null,
        IFunctionsWorkerResolverFactory? workerResolverFactory = null,
        IFunctionsWorkerInstaller? workerInstaller = null,
        IInteractionService? interaction = null,
        IHostProcessRunner? hostProcessRunner = null)
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

        workloadInstaller ??= CreateSuccessfulInstaller();
        workerResolverFactory ??= CreateWorkerResolverFactory();
        workerInstaller ??= CreateSuccessfulWorkerInstaller();
        interaction ??= Substitute.For<IInteractionService>();
        interaction.ConfirmAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(true);
        hostProcessRunner ??= CreateSuccessfulHostProcessRunner();
        IWorkloadPaths workloadPaths = CreateWorkloadPaths();
        IExtensionBundleResolver bundleResolver = Substitute.For<IExtensionBundleResolver>();
        var bundleSectionReader = new HostJsonBundleSectionReader();

        return new DemoStartInitializationRunner(
            projectResolver,
            bundleResolver,
            bundleSectionReader,
            profileResolver,
            hostWorkloadResolver,
            workerResolverFactory,
            workerInstaller,
            workloadInstaller,
            interaction,
            new LocalSettingsProvider(),
            workloadPaths,
            hostProcessRunner,
            new ProcessEnvironment(),
            CreateDisabledAzuriteOrchestrator(),
            NullLoggerFactory.Instance);
    }

    private static IManagedAzuriteOrchestrator CreateDisabledAzuriteOrchestrator()
    {
        IManagedAzuriteOrchestrator orchestrator = Substitute.For<IManagedAzuriteOrchestrator>();
        orchestrator.EnsureReadyAsync(Arg.Any<ManagedAzuriteRequest>(), Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>())
            .Returns(new ManagedAzuriteResult.Disabled("Azurite skipped in tests."));
        return orchestrator;
    }

    private static IFunctionsWorkerResolverFactory CreateWorkerResolverFactory(IFunctionsWorkerResolver? workerResolver = null)
    {
        if (workerResolver is null)
        {
            workerResolver = Substitute.For<IFunctionsWorkerResolver>();
            workerResolver.ResolveWorkerAsync(Arg.Any<FunctionsWorkerId>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    FunctionsWorkerId workerId = callInfo.ArgAt<FunctionsWorkerId>(0);
                    return FunctionsWorkerResolutionResults.Resolved(CreateWorker(workerId.Value, workerId.Value));
                });
        }

        IFunctionsWorkerResolverFactory workerResolverFactory = Substitute.For<IFunctionsWorkerResolverFactory>();
        workerResolverFactory.Create(Arg.Any<IReadOnlyDictionary<string, VersionRange>>())
            .Returns(workerResolver);
        return workerResolverFactory;
    }

    private static IFunctionsWorker CreateWorker(string workerId, string workerRuntime, string version = "1.0.0")
        => new TestFunctionsWorker(new FunctionsWorkerId(workerId), workerRuntime, "worker.config.json", version);

    private static IHostProcessRunner CreateSuccessfulHostProcessRunner()
    {
        IHostProcessRunner runner = Substitute.For<IHostProcessRunner>();
        var eventStream = new InMemoryHostEventStream();
        eventStream.Complete();
        runner.StartAsync(Arg.Any<HostProcessStartContext>(), Arg.Any<CancellationToken>())
            .Returns(eventStream);
        return runner;
    }

    private static IWorkloadPaths CreateWorkloadPaths()
    {
        IWorkloadPaths workloadPaths = Substitute.For<IWorkloadPaths>();
        workloadPaths.GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>())
            .Returns(callInfo => Path.Combine(Path.GetTempPath(), "workloads", (string)callInfo[0], (string)callInfo[1]));
        return workloadPaths;
    }

    private static IWorkloadInstaller CreateSuccessfulInstaller()
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
                string packageId = callInfo.ArgAt<string>(0);
                var version = (NuGetVersion?)callInfo[1];
                string packageVersion = version?.ToNormalizedString() ?? DefaultInstallVersion(packageId);
                WorkloadEntry entry = string.Equals(packageId, HostWorkloadPackage.CurrentPackageId, StringComparison.OrdinalIgnoreCase)
                    ? CreateHostEntry(packageVersion)
                    : CreateWorkerEntry(packageId, packageVersion);
                return new WorkloadInstallResult(entry, AlreadyInstalled: false);
            });

        return workloadInstaller;
    }

    private static IFunctionsWorkerInstaller CreateSuccessfulWorkerInstaller()
    {
        IFunctionsWorkerInstaller workerInstaller = Substitute.For<IFunctionsWorkerInstaller>();
        workerInstaller.InstallAsync(
                Arg.Any<FunctionsWorkerId>(),
                Arg.Any<IReadOnlyDictionary<string, VersionRange>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                FunctionsWorkerId workerId = callInfo.ArgAt<FunctionsWorkerId>(0);
                string packageId = FunctionsWorkerWorkloadPackages.GetPackageId(workerId);
                string packageVersion = DefaultInstallVersion(packageId);
                var workloadInstallResult = new WorkloadInstallResult(CreateWorkerEntry(packageId, packageVersion), AlreadyInstalled: false);
                IFunctionsWorker worker = CreateWorker(workerId.Value, workerId.Value, packageVersion);
                return new FunctionsWorkerInstallResult(worker, workloadInstallResult);
            });

        return workerInstaller;
    }

    private static string DefaultInstallVersion(string packageId)
        => packageId.Contains("worker", StringComparison.OrdinalIgnoreCase) ? "3.13.0" : "4.834.0";

    private static IHostWorkloadResolver CreateInstalledHostWorkloadResolver()
    {
        IHostWorkloadResolver hostWorkloadResolver = Substitute.For<IHostWorkloadResolver>();
        HostWorkloadResolution hostResolution = new HostWorkloadResolution.Installed(
            CreateHostWorkload("4.1000.0"),
            NuGetVersion.Parse("4.1000.0"),
            ExplicitlyRequested: false);
        hostWorkloadResolver.ResolveAsync(Arg.Any<HostWorkloadResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(hostResolution);
        return hostWorkloadResolver;
    }

    private static ProfileResolution CreateResolvedProfile(IReadOnlyDictionary<string, VersionRange> workerVersionRanges)
    {
        var profileSource = new ProfileSourceInfo(ProfileSourceKind.BuiltIn, "bundled");
        var profile = new ResolvedProfile(
            "flex",
            profileSource,
            Sku: "flex",
            ProfileStatus.Stable,
            DeprecationUrl: null,
            VersionRange.Parse("[1.8.1, 4.1048.200)"),
            workerVersionRanges,
            ExtensionBundleVersionRange: null,
            SupportedRuntimes: ["node"],
            Notes: null);
        return new ProfileResolution.Resolved(profile, []);
    }

    private string CreateLocalHostContentRoot()
    {
        string contentRoot = Path.Combine(_tempDir, "local-host-content");
        Directory.CreateDirectory(contentRoot);
        string executableName = OperatingSystem.IsWindows()
            ? $"{HostProcessStartInfoFactory.ExecutableBaseName}.exe"
            : HostProcessStartInfoFactory.ExecutableBaseName;
        File.WriteAllText(Path.Combine(contentRoot, executableName), string.Empty);
        string workersDirectory = Path.Combine(contentRoot, "workers");
        Directory.CreateDirectory(workersDirectory);
        File.WriteAllText(Path.Combine(workersDirectory, "workers.txt"), string.Empty);
        return Path.GetFullPath(contentRoot);
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

    private static WorkloadEntry CreateWorkerEntry(string packageId, string packageVersion)
        => new()
        {
            PackageId = packageId,
            PackageVersion = packageVersion,
            Kind = WorkloadKind.Content,
            Aliases = packageId.EndsWith("-worker", StringComparison.OrdinalIgnoreCase) ? [packageId] : [],
            DisplayName = packageId,
            Description = string.Empty,
        };

    private static FunctionsWorkerResolutionFailure CreateNotInstalledWorkerFailure(string runtime)
    {
        var workerId = new FunctionsWorkerId(runtime);
        string packageId = $"Azure.Functions.Cli.Workloads.Workers.{runtime}";
        return FunctionsWorkerResolutionFailures.NotInstalled(
            workerId,
            $"No installed Azure Functions worker was found for '{runtime}'. Run 'func workload install {packageId} --exact' to install it.");
    }

    private static ProjectResolutionResult CreateWorkerNotResolvedResult(FunctionsWorkerResolutionFailure workerFailure)
    {
        ProjectCreationFailure failure = new ProjectCreationFailure.WorkerNotResolved(workerFailure, workerFailure.Message);
        return ProjectResolutionResults.NotResolved(workerFailure.Message, failure);
    }

    private static ContentWorkloadInfo CreateHostWorkload(string packageVersion)
        => new(
            PackageId: HostWorkloadPackage.CurrentPackageId,
            PackageVersion: packageVersion,
            Aliases: ["host"],
            InstallDirectory: Path.Combine(Path.GetTempPath(), "workloads", HostWorkloadPackage.CurrentPackageId, packageVersion),
            ContentRoot: Path.Combine(Path.GetTempPath(), "workloads", HostWorkloadPackage.CurrentPackageId, packageVersion, "tools", "any"),
            DisplayName: "Azure Functions host",
            Description: string.Empty);

    private static bool HasWorkerRange(IReadOnlyDictionary<string, VersionRange> ranges, VersionRange expectedRange)
        => ranges.TryGetValue("node", out VersionRange? actualRange)
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

        public List<string> ConfirmPrompts { get; } = [];

        public bool ConfirmResult { get; set; } = true;

        public Task OnEventAsync(StartInitializationEvent initializationEvent, CancellationToken cancellationToken)
        {
            Events.Add(initializationEvent);
            return Task.CompletedTask;
        }

        public Task<bool> ConfirmAsync(string prompt, bool defaultValue, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConfirmPrompts.Add(prompt);
            return Task.FromResult(ConfirmResult);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        public EnvironmentVariableScope(string name, string value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previousValue);
        }
    }

    private sealed class TestFunctionsProject(
        WorkingDirectory workingDirectory,
        string stackName,
        string stackDisplayName,
        bool supportsExtensionBundles) : FunctionsProject
    {
        private readonly WorkingDirectory _workingDirectory = workingDirectory;
        private readonly FunctionsWorkerReference _workerReference = FunctionsWorkerReference.FromWorkload(stackName);

        public List<FunctionsProjectHostRunContext> PreparedContexts { get; } = [];

        public Action<FunctionsProjectHostRunContext>? PrepareAction { get; set; }

        public override WorkingDirectory WorkingDirectory => _workingDirectory;

        public override string StackName => stackName;

        public override string StackDisplayName => stackDisplayName;

        public override bool SupportsExtensionBundles => supportsExtensionBundles;

        public override FunctionsWorkerReference WorkerReference => _workerReference;

        public override Task PrepareForHostRunAsync(FunctionsProjectHostRunContext context, CancellationToken cancellationToken)
        {
            PrepareAction?.Invoke(context);
            PreparedContexts.Add(context);
            return Task.CompletedTask;
        }
    }

    private sealed record TestFunctionsWorker(
        FunctionsWorkerId Id,
        string WorkerRuntime,
        string WorkerConfigPath,
        string Version) : IFunctionsWorker;

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
