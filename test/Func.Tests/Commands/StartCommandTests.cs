// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli;
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
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class StartCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TestInteractionService _interaction = new();
    private readonly FunctionPalette _palette = new();
    private readonly IStartInitializationRunner _initializationRunner = Substitute.For<IStartInitializationRunner>();
    private readonly ICliVersionProvider _cliVersionProvider = Substitute.For<ICliVersionProvider>();

    public StartCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-start-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _cliVersionProvider.Version.Returns("5.0.0-test");
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
            Substitute.For<IOptionsMonitor<HostStartupOptions>>());

        var optionNames = cmd.Options.Select(o => o.Name).ToList();

        Assert.Contains("--port", optionNames);
        Assert.Contains("--cors", optionNames);
        Assert.Contains("--cors-credentials", optionNames);
        Assert.Contains("--functions", optionNames);
        Assert.Contains("--no-build", optionNames);
        Assert.Contains("--enable-auth", optionNames);
        Assert.Contains("--host-version", optionNames);
        Assert.Contains("--output", optionNames);
        Assert.Contains("--no-tui", optionNames);
        Assert.Contains("--log-file", optionNames);
    }

    [Fact]
    public void StartCommand_RegisteredInParser()
    {
        var root = TestParser.CreateRoot(_interaction);
        var names = root.Subcommands.Select(c => c.Name).ToList();

        Assert.Contains("start", names);
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
        var ex = await Assert.ThrowsAsync<GracefulException>(() => result.InvokeAsync(config));
        Assert.Contains("does not exist", ex.Message);
        Assert.Contains(nonExistent, ex.Message);
    }

    [Fact]
    public async Task StartCommand_InvalidOutputMode_ThrowsGracefulException()
    {
        var root = TestParser.CreateRoot(_interaction);
        var result = root.Parse($"start \"{_tempDir}\" --output=bogus");

        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        var ex = await Assert.ThrowsAsync<GracefulException>(() => result.InvokeAsync(config));
        Assert.Contains("--output", ex.Message);
        Assert.Contains("bogus", ex.Message);
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
        var result = root.Parse($"start \"{_tempDir}\" --output=plain --host-version 4.900.0 --no-build --enable-auth --port 9090 --functions HttpTrigger --cors http://localhost,http://example --cors-credentials");

        int exitCode = await result.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        Assert.Equal(0, exitCode);
        await _initializationRunner.Received(1).RunAsync(
            Arg.Is<StartInitializationContext>(context =>
                context.Options.WorkingDirectory.Info.FullName == new DirectoryInfo(_tempDir).FullName
                && context.ProfileName == "none"
                && context.CliVersion == "5.0.0-test"
                && context.Options.OutputMode == OutputMode.Plain
                && context.Options.RequestedHostVersion == "4.900.0"
                && context.Options.NoBuild
                && context.Options.EnableAuth
                && context.Options.Port == 9090
                && context.Options.Functions.SequenceEqual(new[] { "HttpTrigger" })
                && context.Options.Cors.SequenceEqual(new[] { "http://localhost", "http://example" })
                && context.Options.CorsCredentials),
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
        options.Get(Arg.Any<string>()).Returns(new HostStartupOptions
        {
            Port = 9091,
            Cors = "http://localhost,http://example",
            CorsCredentials = true,
        });
        var cmd = new StartCommand(
            _interaction,
            _palette,
            _cliVersionProvider,
            _initializationRunner,
            options);
        var root = new RootCommand { cmd };
        var result = root.Parse($"start \"{_tempDir}\" --output=plain");

        int exitCode = await result.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        Assert.Equal(0, exitCode);
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
        options.CurrentValue.Returns(new HostStartupOptions
        {
            Port = 9092,
        });
        var cmd = new StartCommand(
            _interaction,
            _palette,
            _cliVersionProvider,
            _initializationRunner,
            options);
        var root = new RootCommand { cmd };
        string projectDirectory = Environment.CurrentDirectory;
        var result = root.Parse($"start \"{projectDirectory}\" --output=plain");

        int exitCode = await result.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        Assert.Equal(0, exitCode);
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
            CreateHostStartupOptions());
        var root = new RootCommand { cmd };
        var result = root.Parse($"start \"{_tempDir}\" --output=plain");

        int exitCode = await result.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        Assert.Equal(0, exitCode);
        TestFunctionsProject project = Assert.IsType<TestFunctionsProject>(initializationResult.Project);
        FunctionsProjectHostRunCompletionContext completion = Assert.Single(project.CompletionContexts);
        Assert.Same(initializationResult.HostRunContext, completion.RunContext);
        FunctionsProjectHostRunOutcome.Completed completed =
            Assert.IsType<FunctionsProjectHostRunOutcome.Completed>(completion.Outcome);
        Assert.Equal(0, completed.ExitCode);
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
            CreateHostStartupOptions());
        var root = new RootCommand { cmd };
        var result = root.Parse($"start \"{_tempDir}\" --output=plain");

        int exitCode = await result.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        Assert.Equal(0, exitCode);
        Assert.Contains("WARNING: Project cleanup failed: cleanup failed", _interaction.Lines);
    }

    private static StartInitializationResult CreateInitializationResult(
        IHostEventStream eventStream,
        TestFunctionsProject? project = null)
    {
        project ??= new TestFunctionsProject();
        var hostRunContext = new FunctionsProjectHostRunContext(
            project.WorkingDirectory.Info,
            project.Worker.WorkerRuntime,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        return new StartInitializationResult(
            new DashboardRunInfo(CliVersion: "5.0.0-test", ProfileName: "none", StackName: ".NET"),
            eventStream,
            HostVersion: "4.834.0",
            BundleRequired: false,
            BundleVersion: null,
            project,
            hostRunContext);
    }

    private static IOptionsMonitor<HostStartupOptions> CreateHostStartupOptions()
    {
        var options = Substitute.For<IOptionsMonitor<HostStartupOptions>>();
        options.Get(Arg.Any<string>()).Returns(new HostStartupOptions());
        options.CurrentValue.Returns(new HostStartupOptions());
        return options;
    }

    private sealed class TestFunctionsProject : FunctionsProject
    {
        private readonly WorkingDirectory _workingDirectory = WorkingDirectory.FromExplicit(Environment.CurrentDirectory);
        private readonly IFunctionsWorker _worker = new TestFunctionsWorker();

        public List<FunctionsProjectHostRunCompletionContext> CompletionContexts { get; } = [];

        public Exception? CleanupException { get; init; }

        public override WorkingDirectory WorkingDirectory => _workingDirectory;

        public override string StackName => "dotnet-isolated";

        public override string StackDisplayName => ".NET";

        public override bool SupportsExtensionBundles => false;

        public override IFunctionsWorker Worker => _worker;

        public override Task CompleteHostRunAsync(
            FunctionsProjectHostRunCompletionContext context,
            CancellationToken cancellationToken)
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
}
