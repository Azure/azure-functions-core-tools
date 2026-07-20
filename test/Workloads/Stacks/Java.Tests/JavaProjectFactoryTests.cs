// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Workloads.Java.Tests;

public class JavaProjectFactoryTests : IDisposable
{
    private readonly DirectoryInfo _projectDir;
    private readonly IFunctionsWorkerResolver _workerResolver = Substitute.For<IFunctionsWorkerResolver>();

    public JavaProjectFactoryTests()
    {
        _projectDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "func-java-resolver-" + Guid.NewGuid().ToString("N")));
        _workerResolver.ResolveWorkerAsync(Arg.Any<FunctionsWorkerId>(), Arg.Any<CancellationToken>())
            .Returns(FunctionsWorkerResolutionResults.Resolved(CreateWorker("java", "java")));
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
        ProjectCreationResult result = await new JavaProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.NotCreated notCreated = Assert.IsType<ProjectCreationResult.NotCreated>(result);
        Assert.Equal("no Java project fingerprint found", notCreated.Reason);
    }

    [Theory]
    [InlineData("pom.xml", "<project/>")]
    [InlineData("build.gradle", "plugins {}")]
    [InlineData("build.gradle.kts", "plugins {}")]
    public async Task Fingerprint_without_host_json_creates_project(string fileName, string contents)
    {
        WriteFile(fileName, contents);

        ProjectCreationResult result = await new JavaProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        Assert.IsType<ProjectCreationResult.Created>(result);
    }

    [Fact]
    public async Task Source_tree_creates_project()
    {
        Directory.CreateDirectory(Path.Combine(_projectDir.FullName, "src", "main", "java"));

        ProjectCreationResult result = await new JavaProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = Assert.IsType<ProjectCreationResult.Created>(result);
        Assert.Equal("found src/main/java", created.Reason);
    }

    [Fact]
    public async Task Foreign_python_directory_does_not_match()
    {
        WriteFile("host.json", "{}");
        WriteFile("requirements.txt", string.Empty);

        ProjectCreationResult result = await new JavaProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        Assert.IsType<ProjectCreationResult.NotCreated>(result);
    }

    [Fact]
    public async Task Foreign_node_directory_does_not_match()
    {
        WriteFile("host.json", "{}");
        WriteFile("package.json", "{}");

        ProjectCreationResult result = await new JavaProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        Assert.IsType<ProjectCreationResult.NotCreated>(result);
    }

    [Theory]
    [InlineData("pom.xml", "<project/>")]
    [InlineData("build.gradle", "plugins {}")]
    public async Task Match_for_each_fingerprint(string fileName, string contents)
    {
        WriteFile("host.json", "{}");
        WriteFile(fileName, contents);

        ProjectCreationResult result = await new JavaProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = Assert.IsType<ProjectCreationResult.Created>(result);
        Assert.Equal("java", created.Project.StackName);
        Assert.Equal("Java", created.Project.StackDisplayName);
        IFunctionsWorker worker = await ResolveWorkerAsync(created.Project);
        Assert.Equal("java", worker.WorkerRuntime);
        Assert.NotNull(created.Reason);
    }

    [Fact]
    public async Task Pom_takes_precedence_over_gradle_and_sources()
    {
        WriteFile("host.json", "{}");
        WriteFile("pom.xml", "<project/>");
        WriteFile("build.gradle", "plugins {}");
        Directory.CreateDirectory(Path.Combine(_projectDir.FullName, "src", "main", "java"));

        ProjectCreationResult result = await new JavaProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = Assert.IsType<ProjectCreationResult.Created>(result);
        Assert.Equal("found pom.xml", created.Reason);
    }

    [Fact]
    public async Task Host_json_only_does_not_match()
    {
        WriteFile("host.json", "{}");

        ProjectCreationResult result = await new JavaProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        Assert.IsType<ProjectCreationResult.NotCreated>(result);
    }

    [Fact]
    public async Task MatchingDirectory_WhenWorkerNotResolved_WorkerReferenceReportsFailure()
    {
        WriteFile("pom.xml", "<project/>");
        FunctionsWorkerResolutionFailure failure = FunctionsWorkerResolutionFailures.NotInstalled(
            new FunctionsWorkerId("java"),
            "missing java worker");
        _workerResolver.ResolveWorkerAsync(Arg.Any<FunctionsWorkerId>(), Arg.Any<CancellationToken>())
            .Returns(FunctionsWorkerResolutionResults.NotResolved(failure));

        ProjectCreationResult result = await new JavaProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = Assert.IsType<ProjectCreationResult.Created>(result);
        FunctionsWorkerResolutionResult workerResult = await created.Project.WorkerReference.ResolveWorkerAsync(
            new FunctionsWorkerResolutionContext(_workerResolver),
            default);
        FunctionsWorkerResolutionResult.NotResolved notResolved = Assert.IsType<FunctionsWorkerResolutionResult.NotResolved>(workerResult);
        Assert.Same(failure, notResolved.Failure);
    }

    [Fact]
    public async Task NullContext_throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new JavaProjectFactory().TryCreateProjectAsync(null!, default));
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
        FunctionsWorkerResolutionResult.Resolved resolved = Assert.IsType<FunctionsWorkerResolutionResult.Resolved>(result);
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
