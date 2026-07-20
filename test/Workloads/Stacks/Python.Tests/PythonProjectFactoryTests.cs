// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.Python.Tests;

public class PythonProjectFactoryTests : IDisposable
{
    private readonly DirectoryInfo _projectDir;
    private readonly IFunctionsWorkerResolver _workerResolver = Substitute.For<IFunctionsWorkerResolver>();

    public PythonProjectFactoryTests()
    {
        _projectDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "func-python-resolver-" + Guid.NewGuid().ToString("N")));
        _workerResolver.ResolveWorkerAsync(Arg.Any<FunctionsWorkerId>(), Arg.Any<CancellationToken>())
            .Returns(FunctionsWorkerResolutionResults.Resolved(CreateWorker("python", "python")));
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
            // Best-effort cleanup; CI runners may hold file handles briefly.
        }
    }

    [Fact]
    public async Task Empty_directory_does_not_match()
    {
        ProjectCreationResult result = await new PythonProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        result.Should().BeOfType<ProjectCreationResult.NotCreated>()
            .Which.Reason.Should().Be("no Python project fingerprint found");
    }

    [Fact]
    public async Task NonExistent_directory_does_not_match()
    {
        DirectoryInfo missing = new(Path.Combine(_projectDir.FullName, "does-not-exist"));

        ProjectCreationResult result = await new PythonProjectFactory().TryCreateProjectAsync(CreateContext(missing), default);

        result.Should().BeOfType<ProjectCreationResult.NotCreated>();
    }

    [Theory]
    [InlineData("requirements.txt")]
    [InlineData("pyproject.toml")]
    [InlineData("function_app.py")]
    [InlineData("uv.lock")]
    [InlineData("poetry.lock")]
    public async Task Fingerprint_without_host_json_creates_project(string fileName)
    {
        WriteFile(fileName, string.Empty);

        ProjectCreationResult result = await new PythonProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        result.Should().BeOfType<ProjectCreationResult.Created>();
    }

    [Fact]
    public async Task Foreign_stack_directory_does_not_match()
    {
        // host.json + Node fingerprint should not be claimed by the Python factory.
        WriteFile("host.json", "{}");
        WriteFile("package.json", "{}");

        ProjectCreationResult result = await new PythonProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        result.Should().BeOfType<ProjectCreationResult.NotCreated>();
    }

    [Fact]
    public async Task Foreign_go_directory_does_not_match()
    {
        WriteFile("host.json", "{}");
        WriteFile("go.mod", "module example");

        ProjectCreationResult result = await new PythonProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        result.Should().BeOfType<ProjectCreationResult.NotCreated>();
    }

    [Theory]
    [InlineData("function_app.py")]
    [InlineData("requirements.txt")]
    [InlineData("pyproject.toml")]
    [InlineData("uv.lock")]
    [InlineData("poetry.lock")]
    public async Task Match_for_each_fingerprint(string fileName)
    {
        WriteFile("host.json", "{}");
        WriteFile(fileName, string.Empty);

        ProjectCreationResult result = await new PythonProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = result.Should().BeOfType<ProjectCreationResult.Created>().Subject;
        created.Project.StackName.Should().Be("python");
        created.Project.StackDisplayName.Should().Be("Python");
        IFunctionsWorker worker = await ResolveWorkerAsync(created.Project);
        worker.WorkerRuntime.Should().Be("python");
        created.Reason.Should().NotBeNull();
    }

    [Fact]
    public async Task Uv_managed_project_matches_via_pyproject_and_lock()
    {
        // Reproduces the uv layout from issue #4676 / #4705: no requirements.txt,
        // pyproject.toml + uv.lock instead.
        WriteFile("host.json", "{}");
        WriteFile("pyproject.toml", "[project]\nname = \"x\"\n");
        WriteFile("uv.lock", string.Empty);

        ProjectCreationResult result = await new PythonProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        result.Should().BeOfType<ProjectCreationResult.Created>().Which.Reason.Should().Be("found pyproject.toml");
    }

    [Fact]
    public async Task Poetry_managed_project_matches_via_pyproject_and_lock()
    {
        WriteFile("host.json", "{}");
        WriteFile("pyproject.toml", "[tool.poetry]\nname = \"x\"\n");
        WriteFile("poetry.lock", string.Empty);

        ProjectCreationResult result = await new PythonProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        result.Should().BeOfType<ProjectCreationResult.Created>().Which.Reason.Should().Be("found pyproject.toml");
    }

    [Fact]
    public async Task Bare_py_file_with_host_json_matches()
    {
        WriteFile("host.json", "{}");
        WriteFile("loose.py", "print('hi')");

        ProjectCreationResult result = await new PythonProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        result.Should().BeOfType<ProjectCreationResult.Created>().Which.Reason.Should().Contain("*.py");
    }

    [Fact]
    public async Task Host_json_only_does_not_match()
    {
        WriteFile("host.json", "{}");

        ProjectCreationResult result = await new PythonProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        result.Should().BeOfType<ProjectCreationResult.NotCreated>();
    }

    [Fact]
    public async Task MatchingDirectory_WhenWorkerNotResolved_WorkerReferenceReportsFailure()
    {
        WriteFile("requirements.txt", string.Empty);
        FunctionsWorkerResolutionFailure failure = FunctionsWorkerResolutionFailures.NotInstalled(
            new FunctionsWorkerId("python"),
            "missing python worker");
        _workerResolver.ResolveWorkerAsync(Arg.Any<FunctionsWorkerId>(), Arg.Any<CancellationToken>())
            .Returns(FunctionsWorkerResolutionResults.NotResolved(failure));

        ProjectCreationResult result = await new PythonProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = result.Should().BeOfType<ProjectCreationResult.Created>().Subject;
        FunctionsWorkerResolutionResult workerResult = await created.Project.WorkerReference.ResolveWorkerAsync(
            new FunctionsWorkerResolutionContext(_workerResolver),
            default);
        workerResult.Should().BeOfType<FunctionsWorkerResolutionResult.NotResolved>()
            .Which.Failure.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task NullContext_throws()
    {
        await FluentActions.Awaiting(() => new PythonProjectFactory().TryCreateProjectAsync(null!, default)).Should().ThrowAsync<ArgumentNullException>();
    }

    private void WriteFile(string name, string contents)
        => File.WriteAllText(Path.Combine(_projectDir.FullName, name), contents);

    private ProjectCreationContext CreateContext(DirectoryInfo? directory = null)
        => new(WorkingDirectory.FromExplicit((directory ?? _projectDir).FullName));

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
