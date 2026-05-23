// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public class DotNetProjectFactoryTests : IDisposable
{
    private readonly DirectoryInfo _projectDir;
    private readonly IFunctionsWorkerResolver _workerResolver = Substitute.For<IFunctionsWorkerResolver>();
    private readonly IDotnetCliRunner _dotnetCli = Substitute.For<IDotnetCliRunner>();

    public DotNetProjectFactoryTests()
    {
        _projectDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "func-dotnet-resolver-" + Guid.NewGuid().ToString("N")));
        _workerResolver.ResolveWorkerAsync(Arg.Any<FunctionsWorkerId>(), Arg.Any<CancellationToken>())
            .Returns(FunctionsWorkerResolutionResults.Resolved(CreateWorker("dotnet", "dotnet")));
    }

    public void Dispose()
    {
        try
        {
            if (_projectDir.Exists)
            {
                _projectDir.Delete(recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Empty_directory_does_not_match()
    {
        ProjectCreationResult result = await new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.NotCreated notCreated = Assert.IsType<ProjectCreationResult.NotCreated>(result);
        Assert.Equal("no .NET project file or build output found", notCreated.Reason);
    }

    [Theory]
    [InlineData("MyApp.csproj")]
    [InlineData("MyApp.fsproj")]
    public async Task Single_project_file_creates_source_project(string fileName)
    {
        WriteFile(fileName, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        ProjectCreationResult result = await new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = Assert.IsType<ProjectCreationResult.Created>(result);
        DotNetSourceProject sourceProject = Assert.IsType<DotNetSourceProject>(created.Project);
        Assert.Equal($"found {fileName}", created.Reason);
        Assert.Equal("dotnet", sourceProject.StackName);
        Assert.Equal(".NET", sourceProject.StackDisplayName);
        Assert.False(sourceProject.SupportsExtensionBundles);
        Assert.Equal(Path.Combine(_projectDir.FullName, fileName), sourceProject.ProjectFilePath);
    }

    [Theory]
    [InlineData("extensions.csproj")]
    [InlineData("extensions.fsproj")]
    public async Task Extensions_project_file_still_matches(string fileName)
    {
        WriteFile(fileName, "<Project></Project>");

        ProjectCreationResult result = await new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = Assert.IsType<ProjectCreationResult.Created>(result);
        Assert.Equal($"found {fileName}", created.Reason);
    }

    [Fact]
    public async Task Multiple_project_files_does_not_match()
    {
        WriteFile("App1.csproj", "<Project></Project>");
        WriteFile("App2.csproj", "<Project></Project>");

        ProjectCreationResult result = await new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.NotCreated notCreated = Assert.IsType<ProjectCreationResult.NotCreated>(result);
        Assert.Equal("multiple .NET project files found; cannot determine which to use", notCreated.Reason);
    }

    [Fact]
    public async Task Nonexistent_directory_does_not_match()
    {
        var nonexistent = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "func-dotnet-missing-" + Guid.NewGuid().ToString("N")));
        var context = new ProjectCreationContext(WorkingDirectory.FromExplicit(nonexistent.FullName), _workerResolver);

        ProjectCreationResult result = await new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(context, default);

        ProjectCreationResult.NotCreated notCreated = Assert.IsType<ProjectCreationResult.NotCreated>(result);
        Assert.Equal("directory does not exist", notCreated.Reason);
    }

    [Fact]
    public async Task Worker_not_resolved_returns_failed()
    {
        WriteFile("MyApp.csproj", "<Project></Project>");
        FunctionsWorkerResolutionFailure failure = FunctionsWorkerResolutionFailures.NotInstalled(
            new FunctionsWorkerId("dotnet"),
            "missing dotnet worker");
        _workerResolver.ResolveWorkerAsync(Arg.Any<FunctionsWorkerId>(), Arg.Any<CancellationToken>())
            .Returns(FunctionsWorkerResolutionResults.NotResolved(failure));

        ProjectCreationResult result = await new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Failed failed = Assert.IsType<ProjectCreationResult.Failed>(result);
        ProjectCreationFailure.WorkerNotResolved workerFailure =
            Assert.IsType<ProjectCreationFailure.WorkerNotResolved>(failed.Failure);
        Assert.Same(failure, workerFailure.WorkerFailure);
    }

    [Fact]
    public async Task NullContext_throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(null!, default));
    }

    [Fact]
    public async Task Non_dotnet_project_files_are_ignored()
    {
        // .vbproj should not be matched as source or output
        WriteFile("MyApp.vbproj", "<Project></Project>");

        ProjectCreationResult result = await new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(CreateContext(), default);

        Assert.IsType<ProjectCreationResult.NotCreated>(result);
    }

    [Fact]
    public async Task Output_directory_creates_output_project()
    {
        WriteFile("host.json", "{}");
        WriteFile("worker.config.json", "{}");
        WriteFile("MyApp.exe", "");

        ProjectCreationResult result = await new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = Assert.IsType<ProjectCreationResult.Created>(result);
        Assert.IsType<DotNetOutputProject>(created.Project);
        Assert.Equal("found .NET build output (host.json, worker.config.json, .exe)", created.Reason);
    }

    [Fact]
    public async Task Output_directory_missing_exe_does_not_match()
    {
        WriteFile("host.json", "{}");
        WriteFile("worker.config.json", "{}");

        ProjectCreationResult result = await new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(CreateContext(), default);

        Assert.IsType<ProjectCreationResult.NotCreated>(result);
    }

    [Fact]
    public async Task Output_directory_missing_worker_config_does_not_match()
    {
        WriteFile("host.json", "{}");
        WriteFile("MyApp.exe", "");

        ProjectCreationResult result = await new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(CreateContext(), default);

        Assert.IsType<ProjectCreationResult.NotCreated>(result);
    }

    [Fact]
    public async Task Source_project_takes_priority_over_output_signals()
    {
        // Directory has both a project file and output artifacts
        WriteFile("MyApp.csproj", "<Project></Project>");
        WriteFile("host.json", "{}");
        WriteFile("worker.config.json", "{}");
        WriteFile("MyApp.exe", "");

        ProjectCreationResult result = await new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = Assert.IsType<ProjectCreationResult.Created>(result);
        Assert.IsType<DotNetSourceProject>(created.Project);
    }

    private void WriteFile(string name, string contents)
        => File.WriteAllText(Path.Combine(_projectDir.FullName, name), contents);

    private ProjectCreationContext CreateContext()
        => new(WorkingDirectory.FromExplicit(_projectDir.FullName), _workerResolver);

    private static IFunctionsWorker CreateWorker(string workerId, string workerRuntime)
        => new TestFunctionsWorker(new FunctionsWorkerId(workerId), workerRuntime, "worker.config.json", "1.0.0");

    private sealed record TestFunctionsWorker(
        FunctionsWorkerId Id,
        string WorkerRuntime,
        string WorkerConfigPath,
        string Version) : IFunctionsWorker;
}
