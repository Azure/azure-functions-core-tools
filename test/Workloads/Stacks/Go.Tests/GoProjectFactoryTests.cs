// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Workloads.Go.Tests;

public class GoProjectFactoryTests : IDisposable
{
    private readonly DirectoryInfo _projectDir;
    private readonly IFunctionsWorkerResolver _workerResolver = Substitute.For<IFunctionsWorkerResolver>();

    public GoProjectFactoryTests()
    {
        _projectDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "func-go-resolver-" + Guid.NewGuid().ToString("N")));
        _workerResolver.ResolveWorkerAsync(Arg.Any<FunctionsWorkerId>(), Arg.Any<CancellationToken>())
            .Returns(FunctionsWorkerResolutionResults.Resolved(CreateWorker("go", "native")));
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
        ProjectCreationResult result = await new GoProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.NotCreated notCreated = Assert.IsType<ProjectCreationResult.NotCreated>(result);
        Assert.Equal("no Go project fingerprint found", notCreated.Reason);
    }

    [Theory]
    [InlineData("go.mod", "module example")]
    [InlineData("main.go", "package main")]
    public async Task Fingerprint_without_host_json_creates_project(string fileName, string contents)
    {
        WriteFile(fileName, contents);

        ProjectCreationResult result = await new GoProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        Assert.IsType<ProjectCreationResult.Created>(result);
    }

    [Fact]
    public async Task Foreign_python_directory_does_not_match()
    {
        WriteFile("host.json", "{}");
        WriteFile("requirements.txt", string.Empty);

        ProjectCreationResult result = await new GoProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        Assert.IsType<ProjectCreationResult.NotCreated>(result);
    }

    [Fact]
    public async Task Foreign_node_directory_does_not_match()
    {
        WriteFile("host.json", "{}");
        WriteFile("package.json", "{}");

        ProjectCreationResult result = await new GoProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        Assert.IsType<ProjectCreationResult.NotCreated>(result);
    }

    [Theory]
    [InlineData("go.mod", "module example")]
    [InlineData("main.go", "package main")]
    public async Task Match_for_each_fingerprint(string fileName, string contents)
    {
        WriteFile("host.json", "{}");
        WriteFile(fileName, contents);

        ProjectCreationResult result = await new GoProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = Assert.IsType<ProjectCreationResult.Created>(result);
        Assert.Equal("go", created.Project.StackName);
        Assert.Equal("Go", created.Project.StackDisplayName);
        Assert.Equal("native", created.Project.Worker.WorkerRuntime);
        Assert.NotNull(created.Reason);
    }

    [Fact]
    public async Task GoMod_takes_precedence_over_loose_go_files()
    {
        WriteFile("host.json", "{}");
        WriteFile("go.mod", "module example");
        WriteFile("main.go", "package main");

        ProjectCreationResult result = await new GoProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = Assert.IsType<ProjectCreationResult.Created>(result);
        Assert.Equal("found go.mod", created.Reason);
    }

    [Fact]
    public async Task Host_json_only_does_not_match()
    {
        WriteFile("host.json", "{}");

        ProjectCreationResult result = await new GoProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        Assert.IsType<ProjectCreationResult.NotCreated>(result);
    }

    [Fact]
    public async Task MatchingDirectory_WhenWorkerNotResolved_Fails()
    {
        WriteFile("go.mod", "module example");
        FunctionsWorkerResolutionFailure failure = FunctionsWorkerResolutionFailures.NotInstalled(
            new FunctionsWorkerId("go"),
            "missing go worker");
        _workerResolver.ResolveWorkerAsync(Arg.Any<FunctionsWorkerId>(), Arg.Any<CancellationToken>())
            .Returns(FunctionsWorkerResolutionResults.NotResolved(failure));

        ProjectCreationResult result = await new GoProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Failed failed = Assert.IsType<ProjectCreationResult.Failed>(result);
        ProjectCreationFailure.WorkerNotResolved workerFailure =
            Assert.IsType<ProjectCreationFailure.WorkerNotResolved>(failed.Failure);
        Assert.Same(failure, workerFailure.WorkerFailure);
    }

    [Fact]
    public async Task NullContext_throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new GoProjectFactory().TryCreateProjectAsync(null!, default));
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
