// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Workloads.PowerShell.Tests;

public class PowerShellFunctionsProjectTests : IDisposable
{
    private readonly DirectoryInfo _projectDir;
    private readonly IFunctionsWorkerResolver _workerResolver = Substitute.For<IFunctionsWorkerResolver>();

    public PowerShellFunctionsProjectTests()
    {
        _projectDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "func-ps-project-" + Guid.NewGuid().ToString("N")));
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
    public void StackName_is_powershell()
    {
        PowerShellFunctionsProject project = CreateProject();

        Assert.Equal("powershell", project.StackName);
    }

    [Fact]
    public void StackDisplayName_is_PowerShell()
    {
        PowerShellFunctionsProject project = CreateProject();

        Assert.Equal("PowerShell", project.StackDisplayName);
    }

    [Fact]
    public void SupportsExtensionBundles_is_true()
    {
        PowerShellFunctionsProject project = CreateProject();

        Assert.True(project.SupportsExtensionBundles);
    }

    [Fact]
    public async Task WorkerReference_resolves_powershell_worker()
    {
        PowerShellFunctionsProject project = CreateProject();

        FunctionsWorkerResolutionResult result = await project.WorkerReference.ResolveWorkerAsync(
            new FunctionsWorkerResolutionContext(_workerResolver),
            default);

        FunctionsWorkerResolutionResult.Resolved resolved = Assert.IsType<FunctionsWorkerResolutionResult.Resolved>(result);
        Assert.Equal("powershell", resolved.Worker.WorkerRuntime);
    }

    [Fact]
    public async Task PrepareForHostRunAsync_is_noop()
    {
        PowerShellFunctionsProject project = CreateProject();
        FunctionsProjectHostRunContext context = new(
            _projectDir,
            "powershell",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        // Should complete without side-effects.
        await project.PrepareForHostRunAsync(context, default);

        Assert.Equal(_projectDir.FullName, context.StartupDirectory.FullName);
    }

    [Fact]
    public async Task PrepareForHostRunAsync_NullContext_throws()
    {
        PowerShellFunctionsProject project = CreateProject();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => project.PrepareForHostRunAsync(null!, default));
    }

    [Fact]
    public void Constructor_NullWorkingDirectory_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PowerShellFunctionsProject(null!));
    }

    private PowerShellFunctionsProject CreateProject()
        => new(WorkingDirectory.FromExplicit(_projectDir.FullName));

    private static IFunctionsWorker CreateWorker(string workerId, string workerRuntime)
        => new TestFunctionsWorker(new FunctionsWorkerId(workerId), workerRuntime, "worker.config.json", "1.0.0");

    private sealed record TestFunctionsWorker(
        FunctionsWorkerId Id,
        string WorkerRuntime,
        string WorkerConfigPath,
        string Version) : IFunctionsWorker;
}
