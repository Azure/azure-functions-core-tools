// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Xunit;

namespace Azure.Functions.Cli.Workloads.Python.Tests;

public class PythonFunctionsProjectTests : IDisposable
{
    private readonly DirectoryInfo _projectDir;

    public PythonFunctionsProjectTests()
    {
        _projectDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "func-py-project-" + Guid.NewGuid().ToString("N")));
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
    public async Task PrepareForHostRun_creates_venv_and_installs_requirements()
    {
        File.WriteAllText(Path.Combine(_projectDir.FullName, "requirements.txt"), "azure-functions");
        string? venvCreatedAt = null;
        string? pipUsed = null;
        string? requirementsUsed = null;

        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            RunCreateVenv = (_, path, _) =>
            {
                venvCreatedAt = path;
                Directory.CreateDirectory(path);
                return Task.FromResult((0, string.Empty));
            },
            RunPipInstall = (_, pip, req, _) =>
            {
                pipUsed = pip;
                requirementsUsed = req;
                return Task.FromResult((0, string.Empty));
            },
            ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.11"),
            ReadEnvironmentVariable = _ => null,
        };
        FunctionsProjectHostRunContext context = CreateContext();

        await project.PrepareForHostRunAsync(context, default);

        Assert.Equal(Path.Combine(_projectDir.FullName, PythonFunctionsProject.DefaultVenvFolderName), venvCreatedAt);
        Assert.NotNull(pipUsed);
        Assert.Contains(PythonFunctionsProject.DefaultVenvFolderName, pipUsed!);
        Assert.Equal(Path.Combine(_projectDir.FullName, "requirements.txt"), requirementsUsed);
        Assert.Equal("1", context.EnvironmentVariables[PythonFunctionsProject.IsolateWorkerDepsEnvVar]);
    }

    [Fact]
    public async Task PrepareForHostRun_skips_venv_creation_when_already_present()
    {
        Directory.CreateDirectory(Path.Combine(_projectDir.FullName, PythonFunctionsProject.DefaultVenvFolderName));
        bool venvCreated = false;
        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            RunCreateVenv = (_, _, _) =>
            {
                venvCreated = true;
                return Task.FromResult((0, string.Empty));
            },
            RunPipInstall = (_, _, _, _) => Task.FromResult((0, string.Empty)),
            ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.11"),
            ReadEnvironmentVariable = _ => null,
        };

        await project.PrepareForHostRunAsync(CreateContext(), default);

        Assert.False(venvCreated);
    }

    [Fact]
    public async Task PrepareForHostRun_skips_pip_when_no_requirements()
    {
        bool pipRan = false;
        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            RunCreateVenv = (_, path, _) =>
            {
                Directory.CreateDirectory(path);
                return Task.FromResult((0, string.Empty));
            },
            RunPipInstall = (_, _, _, _) =>
            {
                pipRan = true;
                return Task.FromResult((0, string.Empty));
            },
            ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.11"),
            ReadEnvironmentVariable = _ => null,
        };

        await project.PrepareForHostRunAsync(CreateContext(), default);

        Assert.False(pipRan);
    }

    [Fact]
    public async Task PrepareForHostRun_throws_graceful_on_venv_failure()
    {
        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            RunCreateVenv = (_, _, _) => Task.FromResult((1, "python not found")),
            RunPipInstall = (_, _, _, _) => Task.FromResult((0, string.Empty)),
            ReadEnvironmentVariable = _ => null,
        };

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => project.PrepareForHostRunAsync(CreateContext(), default));

        Assert.True(ex.IsUserError);
        Assert.Contains("venv", ex.Message);
        Assert.Contains("python not found", ex.Message);
    }

    [Fact]
    public async Task PrepareForHostRun_throws_graceful_on_pip_failure()
    {
        File.WriteAllText(Path.Combine(_projectDir.FullName, "requirements.txt"), "broken==");
        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            RunCreateVenv = (_, path, _) =>
            {
                Directory.CreateDirectory(path);
                return Task.FromResult((0, string.Empty));
            },
            RunPipInstall = (_, _, _, _) => Task.FromResult((2, "invalid spec")),
            ReadEnvironmentVariable = _ => null,
        };

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => project.PrepareForHostRunAsync(CreateContext(), default));

        Assert.True(ex.IsUserError);
        Assert.Contains("pip install", ex.Message);
        Assert.Contains("invalid spec", ex.Message);
    }

    [Fact]
    public async Task PrepareForHostRun_sets_worker_executable_path_and_runtime_version()
    {
        File.WriteAllText(Path.Combine(_projectDir.FullName, "requirements.txt"), "azure-functions");
        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            RunCreateVenv = (_, path, _) =>
            {
                Directory.CreateDirectory(path);
                return Task.FromResult((0, string.Empty));
            },
            RunPipInstall = (_, _, _, _) => Task.FromResult((0, string.Empty)),
            ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.12"),
            ReadEnvironmentVariable = _ => null,
        };
        FunctionsProjectHostRunContext context = CreateContext();

        await project.PrepareForHostRunAsync(context, default);

        string expectedExe = PythonFunctionsProject.GetVenvExecutablePath(
            Path.Combine(_projectDir.FullName, PythonFunctionsProject.DefaultVenvFolderName),
            "python");

        Assert.Equal(expectedExe, context.EnvironmentVariables[PythonFunctionsProject.WorkerExecutablePathEnvVar]);
        Assert.Equal("3.12", context.EnvironmentVariables[PythonFunctionsProject.WorkerRuntimeVersionEnvVar]);
    }

    [Fact]
    public async Task PrepareForHostRun_omits_runtime_version_when_python_version_unreadable()
    {
        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            RunCreateVenv = (_, path, _) =>
            {
                Directory.CreateDirectory(path);
                return Task.FromResult((0, string.Empty));
            },
            RunPipInstall = (_, _, _, _) => Task.FromResult((0, string.Empty)),
            ReadPythonVersion = (_, _) => Task.FromResult<string?>(null),
            ReadEnvironmentVariable = _ => null,
        };
        FunctionsProjectHostRunContext context = CreateContext();

        await project.PrepareForHostRunAsync(context, default);

        Assert.False(context.EnvironmentVariables.ContainsKey(PythonFunctionsProject.WorkerRuntimeVersionEnvVar));
        Assert.True(context.EnvironmentVariables.ContainsKey(PythonFunctionsProject.WorkerExecutablePathEnvVar));
    }

    [Fact]
    public async Task PrepareForHostRun_uses_VIRTUAL_ENV_when_set_and_skips_create()
    {
        string activatedVenv = Path.Combine(_projectDir.FullName, "my-shared-env");
        Directory.CreateDirectory(activatedVenv);
        bool venvCreated = false;
        string? pipUsed = null;

        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            RunCreateVenv = (_, _, _) =>
            {
                venvCreated = true;
                return Task.FromResult((0, string.Empty));
            },
            RunPipInstall = (_, pip, _, _) =>
            {
                pipUsed = pip;
                return Task.FromResult((0, string.Empty));
            },
            ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.11"),
            ReadEnvironmentVariable = name => name == PythonFunctionsProject.VirtualEnvEnvVar ? activatedVenv : null,
        };
        File.WriteAllText(Path.Combine(_projectDir.FullName, "requirements.txt"), "azure-functions");
        FunctionsProjectHostRunContext context = CreateContext();

        await project.PrepareForHostRunAsync(context, default);

        Assert.False(venvCreated);
        Assert.NotNull(pipUsed);
        Assert.Contains("my-shared-env", pipUsed!);
        Assert.Contains("my-shared-env", context.EnvironmentVariables[PythonFunctionsProject.WorkerExecutablePathEnvVar]);
    }

    [Theory]
    [InlineData("venv")]
    [InlineData("env")]
    [InlineData(".virtualenv")]
    public async Task PrepareForHostRun_reuses_existing_conventional_venv_folder(string folderName)
    {
        Directory.CreateDirectory(Path.Combine(_projectDir.FullName, folderName));
        bool venvCreated = false;
        string? pipUsed = null;

        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            RunCreateVenv = (_, _, _) =>
            {
                venvCreated = true;
                return Task.FromResult((0, string.Empty));
            },
            RunPipInstall = (_, pip, _, _) =>
            {
                pipUsed = pip;
                return Task.FromResult((0, string.Empty));
            },
            ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.11"),
            ReadEnvironmentVariable = _ => null,
        };
        File.WriteAllText(Path.Combine(_projectDir.FullName, "requirements.txt"), "azure-functions");

        await project.PrepareForHostRunAsync(CreateContext(), default);

        Assert.False(venvCreated);
        Assert.NotNull(pipUsed);
        Assert.Contains(folderName, pipUsed!);
    }

    private FunctionsProjectHostRunContext CreateContext()
        => new(_projectDir, "python", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FUNCTIONS_WORKER_RUNTIME"] = "python",
        });
}
