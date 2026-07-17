// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using NSubstitute;

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

        ProjectCreationResult.NotCreated notCreated = result.Should().BeOfType<ProjectCreationResult.NotCreated>().Subject;
        notCreated.Reason.Should().Be("no Go project fingerprint found");
    }

    [Theory]
    [InlineData("go.mod", "module example")]
    [InlineData("main.go", "package main")]
    public async Task Fingerprint_without_host_json_creates_project(string fileName, string contents)
    {
        WriteFile(fileName, contents);

        ProjectCreationResult result = await new GoProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        result.Should().BeOfType<ProjectCreationResult.Created>();
    }

    [Fact]
    public async Task Foreign_python_directory_does_not_match()
    {
        WriteFile("host.json", "{}");
        WriteFile("requirements.txt", string.Empty);

        ProjectCreationResult result = await new GoProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        result.Should().BeOfType<ProjectCreationResult.NotCreated>();
    }

    [Fact]
    public async Task Foreign_node_directory_does_not_match()
    {
        WriteFile("host.json", "{}");
        WriteFile("package.json", "{}");

        ProjectCreationResult result = await new GoProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        result.Should().BeOfType<ProjectCreationResult.NotCreated>();
    }

    [Theory]
    [InlineData("go.mod", "module example")]
    [InlineData("main.go", "package main")]
    public async Task Match_for_each_fingerprint(string fileName, string contents)
    {
        WriteFile("host.json", "{}");
        WriteFile(fileName, contents);

        ProjectCreationResult result = await new GoProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = result.Should().BeOfType<ProjectCreationResult.Created>().Subject;
        created.Project.StackName.Should().Be("go");
        created.Project.StackDisplayName.Should().Be("Go");
        IFunctionsWorker worker = await ResolveWorkerAsync(created.Project);
        worker.WorkerRuntime.Should().Be("native");
        created.Reason.Should().NotBeNull();
    }

    [Fact]
    public async Task GoMod_takes_precedence_over_loose_go_files()
    {
        WriteFile("host.json", "{}");
        WriteFile("go.mod", "module example");
        WriteFile("main.go", "package main");

        ProjectCreationResult result = await new GoProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = result.Should().BeOfType<ProjectCreationResult.Created>().Subject;
        created.Reason.Should().Be("found go.mod");
    }

    [Fact]
    public async Task Host_json_only_does_not_match()
    {
        WriteFile("host.json", "{}");

        ProjectCreationResult result = await new GoProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        result.Should().BeOfType<ProjectCreationResult.NotCreated>();
    }

    [Fact]
    public async Task MatchingDirectory_WhenWorkerNotResolved_WorkerReferenceReportsFailure()
    {
        WriteFile("go.mod", "module example");
        FunctionsWorkerResolutionFailure failure = FunctionsWorkerResolutionFailures.NotInstalled(
            new FunctionsWorkerId("go"),
            "missing go worker");
        _workerResolver.ResolveWorkerAsync(Arg.Any<FunctionsWorkerId>(), Arg.Any<CancellationToken>())
            .Returns(FunctionsWorkerResolutionResults.NotResolved(failure));

        ProjectCreationResult result = await new GoProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = result.Should().BeOfType<ProjectCreationResult.Created>().Subject;
        FunctionsWorkerResolutionResult workerResult = await created.Project.WorkerReference.ResolveWorkerAsync(
            new FunctionsWorkerResolutionContext(_workerResolver),
            default);
        FunctionsWorkerResolutionResult.NotResolved notResolved = workerResult.Should().BeOfType<FunctionsWorkerResolutionResult.NotResolved>().Subject;
        notResolved.Failure.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task NullContext_throws()
    {
        await FluentActions.Awaiting(() => new GoProjectFactory().TryCreateProjectAsync(null!, default)).Should().ThrowAsync<ArgumentNullException>();
    }

    private void WriteFile(string name, string contents)
        => File.WriteAllText(Path.Combine(_projectDir.FullName, name), contents);

    private ProjectCreationContext CreateContext()
        => new(WorkingDirectory.FromExplicit(_projectDir.FullName));

    private async Task<IFunctionsWorker> ResolveWorkerAsync(FunctionsProject project)
    {
        FunctionsWorkerResolutionResult result = await project.WorkerReference.ResolveWorkerAsync(
            new FunctionsWorkerResolutionContext(_workerResolver),
            default);
        FunctionsWorkerResolutionResult.Resolved resolved = result.Should().BeOfType<FunctionsWorkerResolutionResult.Resolved>().Subject;
        return resolved.Worker;
    }

    private static IFunctionsWorker CreateWorker(string workerId, string workerRuntime)
        => new TestFunctionsWorker(new FunctionsWorkerId(workerId), workerRuntime, "worker.config.json", "1.0.0");

    private sealed record TestFunctionsWorker(
        FunctionsWorkerId Id,
        string WorkerRuntime,
        string WorkerConfigPath,
        string Version) : IFunctionsWorker;
}
