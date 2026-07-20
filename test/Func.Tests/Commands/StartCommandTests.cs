// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Commands.Start.Initialization.Rendering;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands;

public class StartCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TestInteractionService _interaction = new();
    private readonly FunctionPalette _palette = new();
    private readonly IStartInitializationRunner _initializationRunner = Substitute.For<IStartInitializationRunner>();
    private readonly ICliVersionProvider _cliVersionProvider = Substitute.For<ICliVersionProvider>();
    private readonly IPlatform _platform = Substitute.For<IPlatform>();
    private readonly CompactDashboardShortcutLabels _shortcutLabels;

    public StartCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-start-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _cliVersionProvider.Version.Returns("5.0.0-test");
        _shortcutLabels = new CompactDashboardShortcutLabels(_platform);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void StartCommand_HasExpectedOptions()
    {
        var cmd = new StartCommand(_interaction, _palette, _cliVersionProvider,
            _initializationRunner,
            Substitute.For<IOptionsMonitor<HostStartupOptions>>(),
            _shortcutLabels,
            _platform);

        var optionNames = cmd.Options.Select(o => o.Name).ToList();

        optionNames.Should().Contain("--port");
        optionNames.Should().Contain("--cors");
        optionNames.Should().Contain("--cors-credentials");
        optionNames.Should().Contain("--functions");
        optionNames.Should().Contain("--no-build");
        optionNames.Should().Contain("--enable-auth");
        optionNames.Should().Contain("--profile");
        optionNames.Should().Contain("--host-version");
        optionNames.Should().Contain("--offline");
        optionNames.Should().Contain("--output");
        optionNames.Should().Contain("--no-tui");
        optionNames.Should().Contain("--log-file");
        optionNames.Should().Contain("--demo");
    }

    [Fact]
    public void StartCommand_RegisteredInParser()
    {
        var root = TestParser.CreateRoot(_interaction);
        var runCommand = root.Subcommands.SingleOrDefault(c => c.Name == "run");

        runCommand.Should().NotBeNull();
        runCommand!.Aliases.Should().Contain("start");
    }

    [Fact]
    public async Task StartCommand_InvokableByRunName()
    {
        var nonExistent = Path.Combine(_tempDir, "does-not-exist");
        var root = TestParser.CreateRoot(_interaction);
        var result = root.Parse($"run \"{nonExistent}\"");

        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        var ex = (await FluentActions.Awaiting(() => result.InvokeAsync(config)).Should().ThrowAsync<GracefulException>()).Which;
        ex.Message.Should().Contain("does not exist");
    }

    [Fact]
    public async Task StartCommand_NonExistentPath_ThrowsGracefulException()
    {
        var nonExistent = Path.Combine(_tempDir, "does-not-exist");
        var root = TestParser.CreateRoot(_interaction);
        var result = root.Parse($"start \"{nonExistent}\"");

        // Disable the default exception handler so GracefulException propagates,
        // matching production wiring.
        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        var ex = (await FluentActions.Awaiting(() => result.InvokeAsync(config)).Should().ThrowAsync<GracefulException>()).Which;
        ex.Message.Should().Contain("does not exist");
        ex.Message.Should().Contain(nonExistent);
    }

    [Fact]
    public async Task StartCommand_InvalidOutputMode_ThrowsGracefulException()
    {
        var root = TestParser.CreateRoot(_interaction);
        var result = root.Parse($"start \"{_tempDir}\" --output=bogus");

        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        var ex = (await FluentActions.Awaiting(() => result.InvokeAsync(config)).Should().ThrowAsync<GracefulException>()).Which;
        ex.Message.Should().Contain("--output");
        ex.Message.Should().Contain("bogus");
    }

    [Fact]
    public async Task StartCommand_RunsInitializationBeforeDashboardPipeline()
    {
        var source = new InMemoryHostEventStream();
        source.Complete();
        StartInitializationResult initializationResult = CreateInitializationResult(source);
        _initializationRunner.RunAsync(
                Arg.Any<StartInitializationContext>(),
                Arg.Any<IStartInitializationRenderer>(),
                Arg.Any<CancellationToken>())
            .Returns(initializationResult);

        IServiceProvider services = TestParser.BuildServiceProviderWith(_interaction, services =>
        {
            services.AddSingleton(_cliVersionProvider);
            services.AddSingleton(_initializationRunner);
        });
        var root = Parser.CreateCommand(services);
        string commandLine = $"start \"{_tempDir}\" --output=plain --profile flex --host-version 4.900.0 --offline "
            + "--no-build --enable-auth --port 9090 --functions HttpTrigger "
            + "--cors http://localhost,http://example --cors-credentials";
        var result = root.Parse(commandLine);

        int exitCode = await result.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        exitCode.Should().Be(0);
        await _initializationRunner.Received(1).RunAsync(
            Arg.Is<StartInitializationContext>(context =>
                context.Options.WorkingDirectory.Info.FullName == new DirectoryInfo(_tempDir).FullName
                && context.ProfileName == "flex"
                && context.CliVersion == "5.0.0-test"
                && context.Options.OutputMode == OutputMode.Plain
                && context.Options.RequestedProfileName == "flex"
                && context.Options.RequestedHostVersion == "4.900.0"
                && context.Options.Offline
                && context.Options.NoBuild
                && context.Options.EnableAuth
                && !context.Options.DemoMode
                && context.Options.Port == 9090
                && context.Options.Functions.SequenceEqual(new[] { "HttpTrigger" })
                && context.Options.Cors.SequenceEqual(new[] { "http://localhost", "http://example" })
                && context.Options.CorsCredentials),
            Arg.Any<IStartInitializationRenderer>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartCommand_DemoSwitch_SetsDemoMode()
    {
        var source = new InMemoryHostEventStream();
        source.Complete();
        StartInitializationResult initializationResult = CreateInitializationResult(source);
        _initializationRunner.RunAsync(
                Arg.Any<StartInitializationContext>(),
                Arg.Any<IStartInitializationRenderer>(),
                Arg.Any<CancellationToken>())
            .Returns(initializationResult);
        var cmd = new StartCommand(
            _interaction,
            _palette,
            _cliVersionProvider,
            _initializationRunner,
            CreateHostStartupOptions(),
            _shortcutLabels,
            _platform);
        var root = new RootCommand { cmd };
        var result = root.Parse($"start \"{_tempDir}\" --output=plain --demo");

        int exitCode = await result.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        exitCode.Should().Be(0);
        await _initializationRunner.Received(1).RunAsync(
            Arg.Is<StartInitializationContext>(context => context.Options.DemoMode),
            Arg.Any<IStartInitializationRenderer>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartCommand_UsesConfiguredHostStartupDefaults_WhenOptionsAreNotProvided()
    {
        var source = new InMemoryHostEventStream();
        source.Complete();
        StartInitializationResult initializationResult = CreateInitializationResult(source);
        _initializationRunner.RunAsync(
                Arg.Any<StartInitializationContext>(),
                Arg.Any<IStartInitializationRenderer>(),
                Arg.Any<CancellationToken>())
            .Returns(initializationResult);
        IOptionsMonitor<HostStartupOptions> options = Substitute.For<IOptionsMonitor<HostStartupOptions>>();
        var startupOptions = new HostStartupOptions
        {
            Port = 9091,
            Cors = "http://localhost,http://example",
            CorsCredentials = true,
        };
        options.Get(Arg.Any<string>()).Returns(startupOptions);
        var cmd = new StartCommand(_interaction, _palette, _cliVersionProvider, _initializationRunner, options, _shortcutLabels, _platform);
        var root = new RootCommand { cmd };
        var result = root.Parse($"start \"{_tempDir}\" --output=plain");

        int exitCode = await result.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        exitCode.Should().Be(0);
        options.Received(1).Get(new DirectoryInfo(_tempDir).FullName);
        await _initializationRunner.Received(1).RunAsync(
            Arg.Is<StartInitializationContext>(context =>
                context.Options.Port == 9091
                && context.Options.Cors.SequenceEqual(new[] { "http://localhost", "http://example" })
                && context.Options.CorsCredentials),
            Arg.Any<IStartInitializationRenderer>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartCommand_UsesCurrentHostStartupOptions_WhenPathIsProjectDirectory()
    {
        var source = new InMemoryHostEventStream();
        source.Complete();
        StartInitializationResult initializationResult = CreateInitializationResult(source);
        _initializationRunner.RunAsync(
                Arg.Any<StartInitializationContext>(),
                Arg.Any<IStartInitializationRenderer>(),
                Arg.Any<CancellationToken>())
            .Returns(initializationResult);
        var options = Substitute.For<IOptionsMonitor<HostStartupOptions>>();
        var startupOptions = new HostStartupOptions
        {
            Port = 9092,
        };
        options.CurrentValue.Returns(startupOptions);
        var cmd = new StartCommand(_interaction, _palette, _cliVersionProvider, _initializationRunner, options, _shortcutLabels, _platform);
        var root = new RootCommand { cmd };
        string projectDirectory = Environment.CurrentDirectory;
        var result = root.Parse($"start \"{projectDirectory}\" --output=plain");

        int exitCode = await result.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        exitCode.Should().Be(0);
        options.DidNotReceive().Get(Arg.Any<string>());
        await _initializationRunner.Received(1).RunAsync(
            Arg.Is<StartInitializationContext>(context => context.Options.Port == 9092),
            Arg.Any<IStartInitializationRenderer>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartCommand_CompletesProjectHostRunAfterDashboardPipeline()
    {
        var source = new InMemoryHostEventStream();
        source.Complete();
        StartInitializationResult initializationResult = CreateInitializationResult(source);
        _initializationRunner.RunAsync(
                Arg.Any<StartInitializationContext>(),
                Arg.Any<IStartInitializationRenderer>(),
                Arg.Any<CancellationToken>())
            .Returns(initializationResult);
        var cmd = new StartCommand(
            _interaction,
            _palette,
            _cliVersionProvider,
            _initializationRunner,
            CreateHostStartupOptions(),
            _shortcutLabels,
            _platform);
        var root = new RootCommand { cmd };
        var result = root.Parse($"start \"{_tempDir}\" --output=plain");

        int exitCode = await result.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        exitCode.Should().Be(0);
        TestFunctionsProject project = initializationResult.Project.Should().BeOfType<TestFunctionsProject>().Subject;
        FunctionsProjectHostRunCompletionContext completion = project.CompletionContexts.Should().ContainSingle().Subject;
        completion.RunContext.Should().BeSameAs(initializationResult.HostRunContext);
        FunctionsProjectHostRunOutcome.Completed completed =
            completion.Outcome.Should().BeOfType<FunctionsProjectHostRunOutcome.Completed>().Subject;
        completed.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task StartCommand_ReturnsHostLifecycleExitCode()
    {
        var source = new CompletedLifecycleEventStream(exitCode: 11);
        StartInitializationResult initializationResult = CreateInitializationResult(source);
        _initializationRunner.RunAsync(
                Arg.Any<StartInitializationContext>(),
                Arg.Any<IStartInitializationRenderer>(),
                Arg.Any<CancellationToken>())
            .Returns(initializationResult);
        var cmd = new StartCommand(
            _interaction,
            _palette,
            _cliVersionProvider,
            _initializationRunner,
            CreateHostStartupOptions(),
            _shortcutLabels,
            _platform);
        var root = new RootCommand { cmd };
        var result = root.Parse($"start \"{_tempDir}\" --output=plain");

        int exitCode = await result.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        exitCode.Should().Be(11);
        TestFunctionsProject project = initializationResult.Project.Should().BeOfType<TestFunctionsProject>().Subject;
        FunctionsProjectHostRunCompletionContext completion = project.CompletionContexts.Should().ContainSingle().Subject;
        FunctionsProjectHostRunOutcome.Completed completed =
            completion.Outcome.Should().BeOfType<FunctionsProjectHostRunOutcome.Completed>().Subject;
        completed.ExitCode.Should().Be(11);
    }

    [Fact]
    public async Task StartCommand_ProjectCleanupFailure_DoesNotReplaceExitCode()
    {
        var source = new InMemoryHostEventStream();
        source.Complete();
        var project = new TestFunctionsProject
        {
            CleanupException = new InvalidOperationException("cleanup failed"),
        };
        StartInitializationResult initializationResult = CreateInitializationResult(source, project);
        _initializationRunner.RunAsync(
                Arg.Any<StartInitializationContext>(),
                Arg.Any<IStartInitializationRenderer>(),
                Arg.Any<CancellationToken>())
            .Returns(initializationResult);
        var cmd = new StartCommand(
            _interaction,
            _palette,
            _cliVersionProvider,
            _initializationRunner,
            CreateHostStartupOptions(),
            _shortcutLabels,
            _platform);
        var root = new RootCommand { cmd };
        var result = root.Parse($"start \"{_tempDir}\" --output=plain");

        int exitCode = await result.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        exitCode.Should().Be(0);
        _interaction.Lines.Should().Contain("WARNING: Project cleanup failed: cleanup failed");
    }

    private static StartInitializationResult CreateInitializationResult(IHostEventStream eventStream, TestFunctionsProject? project = null)
    {
        project ??= new TestFunctionsProject();
        var hostRunContext = new FunctionsProjectHostRunContext(
            project.WorkingDirectory.Info,
            project.TestWorker.WorkerRuntime,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var runInfo = new DashboardRunInfo(CliVersion: "5.0.0-test", ProfileName: "none", StackName: ".NET");
        return new StartInitializationResult(
            runInfo,
            eventStream,
            HostVersion: "4.834.0",
            BundleRequired: false,
            BundleVersion: null,
            project,
            project.TestWorker,
            hostRunContext);
    }

    private static IOptionsMonitor<HostStartupOptions> CreateHostStartupOptions()
    {
        var options = Substitute.For<IOptionsMonitor<HostStartupOptions>>();
        var startupOptions = new HostStartupOptions();
        options.Get(Arg.Any<string>()).Returns(startupOptions);
        options.CurrentValue.Returns(startupOptions);
        return options;
    }

    private sealed class TestFunctionsProject : FunctionsProject
    {
        private readonly WorkingDirectory _workingDirectory = WorkingDirectory.FromExplicit(Environment.CurrentDirectory);
        private readonly FunctionsWorkerReference _workerReference = FunctionsWorkerReference.FromWorkload("dotnet-isolated");

        public List<FunctionsProjectHostRunCompletionContext> CompletionContexts { get; } = [];

        public Exception? CleanupException { get; init; }

        public IFunctionsWorker TestWorker { get; } = new TestFunctionsWorker();

        public override WorkingDirectory WorkingDirectory => _workingDirectory;

        public override string StackName => "dotnet-isolated";

        public override string StackDisplayName => ".NET";

        public override bool SupportsExtensionBundles => false;

        public override FunctionsWorkerReference WorkerReference => _workerReference;

        public override Task CompleteHostRunAsync(FunctionsProjectHostRunCompletionContext context, CancellationToken cancellationToken)
        {
            CompletionContexts.Add(context);
            if (CleanupException is not null)
            {
                throw CleanupException;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TestFunctionsWorker : IFunctionsWorker
    {
        public FunctionsWorkerId Id => new("dotnet-isolated");

        public string WorkerRuntime => "dotnet-isolated";

        public string WorkerConfigPath => "worker.config.json";

        public string Version => "1.0.0";
    }

    private sealed class CompletedLifecycleEventStream(int exitCode) : IHostEventStream, IHostEventStreamLifecycle
    {
        public async IAsyncEnumerable<HostLogEntry> ReadAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task RequestShutdownAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken)
            => Task.FromResult(exitCode);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
