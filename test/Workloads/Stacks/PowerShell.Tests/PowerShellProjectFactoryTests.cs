// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Workloads.PowerShell.Tests;

public class PowerShellProjectFactoryTests : IDisposable
{
    private readonly DirectoryInfo _projectDir;
    private readonly IFunctionsWorkerResolver _workerResolver = Substitute.For<IFunctionsWorkerResolver>();

    public PowerShellProjectFactoryTests()
    {
        _projectDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "func-ps-resolver-" + Guid.NewGuid().ToString("N")));
        _workerResolver.ResolveWorkerAsync(Arg.Any<FunctionsWorkerId>(), Arg.Any<CancellationToken>())
            .Returns(FunctionsWorkerResolutionResults.Resolved(CreateWorker("powershell", "powershell")));
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
        ProjectCreationResult result = await new PowerShellProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.NotCreated notCreated = Assert.IsType<ProjectCreationResult.NotCreated>(result);
        Assert.Equal("no PowerShell project fingerprint found", notCreated.Reason);
    }

    [Theory]
    [InlineData("profile.ps1", "# profile")]
    [InlineData("requirements.psd1", "@{}")]
    [InlineData("run.ps1", "param($Request)")]
    public async Task Fingerprint_creates_project(string fileName, string contents)
    {
        WriteFile(fileName, contents);

        ProjectCreationResult result = await new PowerShellProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        Assert.IsType<ProjectCreationResult.Created>(result);
    }

    [Fact]
    public async Task Modules_folder_creates_project()
    {
        Directory.CreateDirectory(Path.Combine(_projectDir.FullName, "Modules"));

        ProjectCreationResult result = await new PowerShellProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = Assert.IsType<ProjectCreationResult.Created>(result);
        Assert.Equal("found Modules folder", created.Reason);
    }

    [Fact]
    public async Task Profile_takes_precedence_over_other_signals()
    {
        WriteFile("profile.ps1", "# profile");
        WriteFile("requirements.psd1", "@{}");
        WriteFile("run.ps1", "param($Request)");

        ProjectCreationResult result = await new PowerShellProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = Assert.IsType<ProjectCreationResult.Created>(result);
        Assert.Equal("found profile.ps1", created.Reason);
    }

    [Fact]
    public async Task Foreign_python_directory_does_not_match()
    {
        WriteFile("requirements.txt", string.Empty);
        WriteFile("function_app.py", "import azure.functions");

        ProjectCreationResult result = await new PowerShellProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        Assert.IsType<ProjectCreationResult.NotCreated>(result);
    }

    [Fact]
    public async Task Foreign_node_directory_does_not_match()
    {
        WriteFile("package.json", "{}");
        WriteFile("index.js", "module.exports = async function() {}");

        ProjectCreationResult result = await new PowerShellProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        Assert.IsType<ProjectCreationResult.NotCreated>(result);
    }

    [Theory]
    [InlineData("profile.ps1", "# profile", "found profile.ps1")]
    [InlineData("requirements.psd1", "@{}", "found requirements.psd1")]
    public async Task Match_for_each_fingerprint_sets_correct_stack(string fileName, string contents, string expectedReason)
    {
        WriteFile(fileName, contents);

        ProjectCreationResult result = await new PowerShellProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = Assert.IsType<ProjectCreationResult.Created>(result);
        Assert.Equal("powershell", created.Project.StackName);
        Assert.Equal("PowerShell", created.Project.StackDisplayName);
        Assert.Equal(expectedReason, created.Reason);
        IFunctionsWorker worker = await ResolveWorkerAsync(created.Project);
        Assert.Equal("powershell", worker.WorkerRuntime);
    }

    [Fact]
    public async Task MatchingDirectory_WhenWorkerNotResolved_WorkerReferenceReportsFailure()
    {
        WriteFile("profile.ps1", "# profile");
        FunctionsWorkerResolutionFailure failure = FunctionsWorkerResolutionFailures.NotInstalled(
            new FunctionsWorkerId("powershell"),
            "missing powershell worker");
        _workerResolver.ResolveWorkerAsync(Arg.Any<FunctionsWorkerId>(), Arg.Any<CancellationToken>())
            .Returns(FunctionsWorkerResolutionResults.NotResolved(failure));

        ProjectCreationResult result = await new PowerShellProjectFactory().TryCreateProjectAsync(CreateContext(), default);

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
            () => new PowerShellProjectFactory().TryCreateProjectAsync(null!, default));
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
