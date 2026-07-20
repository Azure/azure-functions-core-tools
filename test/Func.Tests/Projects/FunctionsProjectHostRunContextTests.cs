// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Tests.Projects;

public sealed class FunctionsProjectHostRunContextTests
{
    [Fact]
    public void Constructor_SetsWorkerRuntimeEnvironmentVariable()
    {
        Dictionary<string, string> environmentVariables = [];

        var context = new FunctionsProjectHostRunContext(
            new DirectoryInfo(Environment.CurrentDirectory),
            "dotnet-isolated",
            environmentVariables);

        context.WorkerRuntime.Should().Be("dotnet-isolated");
        environmentVariables[FunctionsProjectHostRunContext.WorkerRuntimeEnvironmentVariable].Should().Be("dotnet-isolated");
    }

    [Fact]
    public void WorkerRuntime_Set_UpdatesEnvironmentVariable()
    {
        var context = new FunctionsProjectHostRunContext(
            new DirectoryInfo(Environment.CurrentDirectory),
            "dotnet-isolated",
            new Dictionary<string, string>());

        context.WorkerRuntime = "python";

        context.WorkerRuntime.Should().Be("python");
        context.EnvironmentVariables[FunctionsProjectHostRunContext.WorkerRuntimeEnvironmentVariable].Should().Be("python");
    }

    [Fact]
    public void WorkerRuntime_WhenRemoved_Throws()
    {
        var context = new FunctionsProjectHostRunContext(
            new DirectoryInfo(Environment.CurrentDirectory),
            "dotnet-isolated",
            new Dictionary<string, string>());

        context.EnvironmentVariables.Remove(FunctionsProjectHostRunContext.WorkerRuntimeEnvironmentVariable);

        FluentActions.Invoking(() => context.WorkerRuntime).Should().ThrowExactly<InvalidOperationException>();
    }

    [Fact]
    public void StartupDirectory_SetToNull_Throws()
    {
        var context = new FunctionsProjectHostRunContext(
            new DirectoryInfo(Environment.CurrentDirectory),
            "dotnet-isolated",
            new Dictionary<string, string>());

        FluentActions.Invoking(() => context.StartupDirectory = null!).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ProvidesNonNullReporter()
    {
        var context = new FunctionsProjectHostRunContext(
            new DirectoryInfo(Environment.CurrentDirectory),
            "dotnet-isolated",
            new Dictionary<string, string>());

        context.Reporter.Should().NotBeNull();
        context.Reporter.Should().BeOfType<NullFunctionsProjectHostRunReporter>();
    }

    [Fact]
    public void Reporter_SetToNull_Throws()
    {
        var context = new FunctionsProjectHostRunContext(
            new DirectoryInfo(Environment.CurrentDirectory),
            "dotnet-isolated",
            new Dictionary<string, string>());

        FluentActions.Invoking(() => context.Reporter = null!).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void NullFunctionsProjectHostRunReporter_Methods_DoNotThrow()
    {
        IFunctionsProjectHostRunReporter reporter = NullFunctionsProjectHostRunReporter.Instance;

        reporter.ReportStatus("working");
        reporter.WriteLog("line", FunctionsProjectReportSeverity.Warning);
    }

    [Fact]
    public async Task FunctionsProject_DefaultLifecycleHooks_CompleteSuccessfully()
    {
        var project = new TestFunctionsProject();
        var hostRunContext = new FunctionsProjectHostRunContext(
            project.WorkingDirectory.Info,
            project.TestWorker.WorkerRuntime,
            new Dictionary<string, string>());
        var completionContext = new FunctionsProjectHostRunCompletionContext(
            hostRunContext,
            FunctionsProjectHostRunOutcomes.Completed(0));

        await project.PrepareForHostRunAsync(hostRunContext, CancellationToken.None);
        await project.CompleteHostRunAsync(completionContext, CancellationToken.None);
    }

    private sealed class TestFunctionsProject : FunctionsProject
    {
        private readonly WorkingDirectory _workingDirectory = WorkingDirectory.FromExplicit(Environment.CurrentDirectory);
        private readonly FunctionsWorkerReference _workerReference = FunctionsWorkerReference.FromWorkload("dotnet-isolated");

        public IFunctionsWorker TestWorker { get; } = new TestFunctionsWorker();

        public override WorkingDirectory WorkingDirectory => _workingDirectory;

        public override string StackName => "dotnet-isolated";

        public override string StackDisplayName => ".NET";

        public override bool SupportsExtensionBundles => false;

        public override FunctionsWorkerReference WorkerReference => _workerReference;
    }

    private sealed class TestFunctionsWorker : IFunctionsWorker
    {
        public FunctionsWorkerId Id => new("dotnet-isolated");

        public string WorkerRuntime => "dotnet-isolated";

        public string WorkerConfigPath => "worker.config.json";

        public string Version => "1.0.0";
    }
}
