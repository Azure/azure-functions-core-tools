// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;

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
            GetRuntimeIdentifier = () => DefaultRid,
            RunCreateVenv = (_, path, _, _, _) =>
            {
                venvCreatedAt = path;
                Directory.CreateDirectory(path);
                return Task.FromResult(0);
            },
            RunPipInstall = (_, pip, req, _, _, _) =>
            {
                pipUsed = pip;
                requirementsUsed = req;
                return Task.FromResult(0);
            },
            ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.11"),
            ReadEnvironmentVariable = _ => null,
        };
        FunctionsProjectHostRunContext context = CreateContext();

        await project.PrepareForHostRunAsync(context, default);

        venvCreatedAt.Should().Be(Path.Combine(_projectDir.FullName, PythonFunctionsProject.DefaultVenvFolderName));
        pipUsed.Should().NotBeNull();
        pipUsed!.Should().Contain(PythonFunctionsProject.DefaultVenvFolderName);
        requirementsUsed.Should().Be(Path.Combine(_projectDir.FullName, "requirements.txt"));
        context.EnvironmentVariables[PythonFunctionsProject.IsolateWorkerDepsEnvVar].Should().Be("1");
    }

    [Fact]
    public async Task PrepareForHostRun_skips_venv_creation_when_already_present()
    {
        Directory.CreateDirectory(Path.Combine(_projectDir.FullName, PythonFunctionsProject.DefaultVenvFolderName));
        bool venvCreated = false;
        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            GetRuntimeIdentifier = () => DefaultRid,
            RunCreateVenv = (_, _, _, _, _) =>
            {
                venvCreated = true;
                return Task.FromResult(0);
            },
            RunPipInstall = (_, _, _, _, _, _) => Task.FromResult(0),
            ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.11"),
            ReadEnvironmentVariable = _ => null,
        };

        await project.PrepareForHostRunAsync(CreateContext(), default);

        venvCreated.Should().BeFalse();
    }

    [Fact]
    public async Task PrepareForHostRun_skips_pip_when_no_requirements()
    {
        bool pipRan = false;
        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            GetRuntimeIdentifier = () => DefaultRid,
            RunCreateVenv = (_, path, _, _, _) =>
            {
                Directory.CreateDirectory(path);
                return Task.FromResult(0);
            },
            RunPipInstall = (_, _, _, _, _, _) =>
            {
                pipRan = true;
                return Task.FromResult(0);
            },
            ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.11"),
            ReadEnvironmentVariable = _ => null,
        };

        await project.PrepareForHostRunAsync(CreateContext(), default);

        pipRan.Should().BeFalse();
    }

    [Fact]
    public async Task PrepareForHostRun_generic_project_reuses_activated_environment_without_uv()
    {
        File.WriteAllText(Path.Combine(_projectDir.FullName, "pyproject.toml"), "[project]\nname = \"app\"\n");
        string activatedVenv = Path.Combine(_projectDir.FullName, "my-shared-env");
        CreateInterpreter(activatedVenv);
        bool venvCreated = false;
        bool uvSyncRan = false;
        bool pipRan = false;
        FunctionsProjectHostRunContext context = CreateContext();

        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            GetRuntimeIdentifier = () => DefaultRid,
            FindUv = () => FakeUvPath,
            RunUvSync = (_, _, _, _, _) =>
            {
                uvSyncRan = true;
                throw new InvalidOperationException("uv sync must not run");
            },
            RunUvExport = (_, _, _, _, _) => throw new InvalidOperationException("uv export must not run"),
            RunCreateVenv = (_, _, _, _, _) =>
            {
                venvCreated = true;
                return Task.FromResult(0);
            },
            RunPipInstall = (_, _, _, _, _, _) =>
            {
                pipRan = true;
                return Task.FromResult(0);
            },
            ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.13"),
            ReadEnvironmentVariable = name => name == PythonFunctionsProject.VirtualEnvEnvVar ? activatedVenv : null,
        };

        await project.PrepareForHostRunAsync(context, default);

        uvSyncRan.Should().BeFalse();
        venvCreated.Should().BeFalse();
        pipRan.Should().BeFalse();
        context.EnvironmentVariables[PythonFunctionsProject.WorkerExecutablePathEnvVar]
            .Should().Be(PythonFunctionsProject.GetVenvExecutablePath(activatedVenv, "python"));
    }

    [Fact]
    public async Task PrepareForHostRun_streams_venv_output_to_reporter()
    {
        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            GetRuntimeIdentifier = () => DefaultRid,
            RunCreateVenv = (_, path, onOut, onErr, _) =>
            {
                Directory.CreateDirectory(path);
                onOut("created virtual environment");
                onErr("venv warning");
                return Task.FromResult(0);
            },
            RunPipInstall = (_, _, _, _, _, _) => Task.FromResult(0),
            ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.11"),
            ReadEnvironmentVariable = _ => null,
        };
        var reporter = new CapturingReporter();
        FunctionsProjectHostRunContext context = CreateContext();
        context.Reporter = reporter;

        await project.PrepareForHostRunAsync(context, default);

        reporter.Logs.Should().Contain(e => e.Severity == FunctionsProjectReportSeverity.Info && e.Line == "created virtual environment");
        reporter.Logs.Should().Contain(e => e.Severity == FunctionsProjectReportSeverity.Error && e.Line == "venv warning");
    }

    [Fact]
    public async Task PrepareForHostRun_throws_graceful_on_venv_failure()
    {
        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            GetRuntimeIdentifier = () => DefaultRid,
            RunCreateVenv = (_, _, _, onErr, _) =>
            {
                onErr("python not found");
                return Task.FromResult(1);
            },
            RunPipInstall = (_, _, _, _, _, _) => Task.FromResult(0),
            ReadEnvironmentVariable = _ => null,
        };
        var reporter = new CapturingReporter();
        FunctionsProjectHostRunContext context = CreateContext();
        context.Reporter = reporter;

        GracefulException ex = (await FluentActions.Awaiting(() => project.PrepareForHostRunAsync(context, default)).Should().ThrowAsync<GracefulException>()).Which;

        ex.IsUserError.Should().BeTrue();
        ex.Message.Should().Contain("venv");
        ex.Message.Should().Contain("exit 1");
        reporter.Logs.Should().Contain(e => e.Severity == FunctionsProjectReportSeverity.Error && e.Line == "python not found");
    }

    [Fact]
    public async Task PrepareForHostRun_throws_graceful_on_pip_failure()
    {
        File.WriteAllText(Path.Combine(_projectDir.FullName, "requirements.txt"), "broken==");
        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            GetRuntimeIdentifier = () => DefaultRid,
            RunCreateVenv = (_, path, _, _, _) =>
            {
                Directory.CreateDirectory(path);
                return Task.FromResult(0);
            },
            RunPipInstall = (_, _, _, _, onErr, _) =>
            {
                onErr("invalid spec");
                return Task.FromResult(2);
            },
            ReadEnvironmentVariable = _ => null,
        };
        var reporter = new CapturingReporter();
        FunctionsProjectHostRunContext context = CreateContext();
        context.Reporter = reporter;

        GracefulException ex = (await FluentActions.Awaiting(() => project.PrepareForHostRunAsync(context, default)).Should().ThrowAsync<GracefulException>()).Which;

        ex.IsUserError.Should().BeTrue();
        ex.Message.Should().Contain("pip install");
        ex.Message.Should().Contain("exit 2");
        reporter.Logs.Should().Contain(e => e.Severity == FunctionsProjectReportSeverity.Error && e.Line == "invalid spec");
    }

    [Theory]
    [InlineData("3.12")]
    [InlineData("3.15")]
    public async Task PrepareForHostRun_sets_worker_executable_path_and_runtime_version(string pythonVersion)
    {
        File.WriteAllText(Path.Combine(_projectDir.FullName, "requirements.txt"), "azure-functions");
        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            GetRuntimeIdentifier = () => DefaultRid,
            RunCreateVenv = (_, path, _, _, _) =>
            {
                Directory.CreateDirectory(path);
                return Task.FromResult(0);
            },
            RunPipInstall = (_, _, _, _, _, _) => Task.FromResult(0),
            ReadPythonVersion = (_, _) => Task.FromResult<string?>(pythonVersion),
            ReadEnvironmentVariable = _ => null,
        };
        FunctionsProjectHostRunContext context = CreateContext();

        await project.PrepareForHostRunAsync(context, default);

        string expectedExe = PythonFunctionsProject.GetVenvExecutablePath(
            Path.Combine(_projectDir.FullName, PythonFunctionsProject.DefaultVenvFolderName),
            "python");

        context.EnvironmentVariables[PythonFunctionsProject.WorkerExecutablePathEnvVar].Should().Be(expectedExe);
        context.EnvironmentVariables[PythonFunctionsProject.WorkerRuntimeVersionEnvVar].Should().Be(pythonVersion);
    }

    [Fact]
    public async Task PrepareForHostRun_omits_runtime_version_when_python_version_unreadable()
    {
        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            GetRuntimeIdentifier = () => DefaultRid,
            RunCreateVenv = (_, path, _, _, _) =>
            {
                Directory.CreateDirectory(path);
                return Task.FromResult(0);
            },
            RunPipInstall = (_, _, _, _, _, _) => Task.FromResult(0),
            ReadPythonVersion = (_, _) => Task.FromResult<string?>(null),
            ReadEnvironmentVariable = _ => null,
        };
        FunctionsProjectHostRunContext context = CreateContext();

        await project.PrepareForHostRunAsync(context, default);

        context.EnvironmentVariables.ContainsKey(PythonFunctionsProject.WorkerRuntimeVersionEnvVar).Should().BeFalse();
        context.EnvironmentVariables.ContainsKey(PythonFunctionsProject.WorkerExecutablePathEnvVar).Should().BeTrue();
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
            GetRuntimeIdentifier = () => DefaultRid,
            RunCreateVenv = (_, _, _, _, _) =>
            {
                venvCreated = true;
                return Task.FromResult(0);
            },
            RunPipInstall = (_, pip, _, _, _, _) =>
            {
                pipUsed = pip;
                return Task.FromResult(0);
            },
            ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.11"),
            ReadEnvironmentVariable = name => name == PythonFunctionsProject.VirtualEnvEnvVar ? activatedVenv : null,
        };
        File.WriteAllText(Path.Combine(_projectDir.FullName, "requirements.txt"), "azure-functions");
        FunctionsProjectHostRunContext context = CreateContext();

        await project.PrepareForHostRunAsync(context, default);

        venvCreated.Should().BeFalse();
        pipUsed.Should().NotBeNull();
        pipUsed!.Should().Contain("my-shared-env");
        context.EnvironmentVariables[PythonFunctionsProject.WorkerExecutablePathEnvVar].Should().Contain("my-shared-env");
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
            GetRuntimeIdentifier = () => DefaultRid,
            RunCreateVenv = (_, _, _, _, _) =>
            {
                venvCreated = true;
                return Task.FromResult(0);
            },
            RunPipInstall = (_, pip, _, _, _, _) =>
            {
                pipUsed = pip;
                return Task.FromResult(0);
            },
            ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.11"),
            ReadEnvironmentVariable = _ => null,
        };
        File.WriteAllText(Path.Combine(_projectDir.FullName, "requirements.txt"), "azure-functions");

        await project.PrepareForHostRunAsync(CreateContext(), default);

        venvCreated.Should().BeFalse();
        pipUsed.Should().NotBeNull();
        pipUsed!.Should().Contain(folderName);
    }

    [Fact]
    public async Task PrepareForHostRun_uv_project_runs_sync_and_never_uses_pip()
    {
        CreateUvProjectFiles();
        CreateInterpreter(DefaultEnvPath);
        string? syncRoot = null;
        string? syncUv = null;
        PythonFunctionsProject project = CreateUvProject();
        project.RunUvSync = (root, uv, _, _, _) =>
        {
            syncRoot = root;
            syncUv = uv;
            return Task.FromResult(0);
        };
        FunctionsProjectHostRunContext context = CreateContext();

        await project.PrepareForHostRunAsync(context, default);

        syncRoot.Should().Be(_projectDir.FullName);
        syncUv.Should().Be(FakeUvPath);
        context.EnvironmentVariables[PythonFunctionsProject.IsolateWorkerDepsEnvVar].Should().Be("1");
        context.EnvironmentVariables[PythonFunctionsProject.WorkerExecutablePathEnvVar]
            .Should().Be(PythonFunctionsProject.GetVenvExecutablePath(DefaultEnvPath, "python"));
    }

    [Fact]
    public async Task PrepareForHostRun_uv_sync_failure_throws_graceful_with_hint()
    {
        CreateUvProjectFiles();
        PythonFunctionsProject project = CreateUvProject();
        project.RunUvSync = (_, _, _, onErr, _) =>
        {
            onErr("No solution found when resolving dependencies");
            return Task.FromResult(1);
        };
        var reporter = new CapturingReporter();
        FunctionsProjectHostRunContext context = CreateContext();
        context.Reporter = reporter;

        GracefulException ex = (await FluentActions.Awaiting(() => project.PrepareForHostRunAsync(context, default)).Should().ThrowAsync<GracefulException>()).Which;

        ex.IsUserError.Should().BeTrue();
        ex.Message.Should().Contain("'uv sync' failed (exit 1)");
        ex.Message.Should().Contain($"Run 'uv sync' in {_projectDir.FullName}");
        reporter.Logs.Should().Contain(e => e.Line == "No solution found when resolving dependencies");
    }

    [Theory]
    [InlineData("custom-env")]
    [InlineData(null)]
    public async Task PrepareForHostRun_uv_uses_UV_PROJECT_ENVIRONMENT(string? relative)
    {
        CreateUvProjectFiles();
        string envPath = relative is null
            ? Path.Combine(_projectDir.FullName, "abs-env")
            : Path.Combine(_projectDir.FullName, relative);
        CreateInterpreter(envPath);
        string configured = relative ?? envPath;
        PythonFunctionsProject project = CreateUvProject();
        project.ReadEnvironmentVariable = name => name == PythonFunctionsProject.UvProjectEnvironmentEnvVar ? configured : null;
        FunctionsProjectHostRunContext context = CreateContext();

        await project.PrepareForHostRunAsync(context, default);

        context.EnvironmentVariables[PythonFunctionsProject.WorkerExecutablePathEnvVar]
            .Should().Be(PythonFunctionsProject.GetVenvExecutablePath(envPath, "python"));
    }

    [Fact]
    public async Task PrepareForHostRun_uv_ignores_VIRTUAL_ENV()
    {
        CreateUvProjectFiles();
        CreateInterpreter(DefaultEnvPath);
        string other = Path.Combine(_projectDir.FullName, "other-env");
        CreateInterpreter(other);
        PythonFunctionsProject project = CreateUvProject();
        project.ReadEnvironmentVariable = name => name == PythonFunctionsProject.VirtualEnvEnvVar ? other : null;
        FunctionsProjectHostRunContext context = CreateContext();

        await project.PrepareForHostRunAsync(context, default);

        context.EnvironmentVariables[PythonFunctionsProject.WorkerExecutablePathEnvVar]
            .Should().Be(PythonFunctionsProject.GetVenvExecutablePath(DefaultEnvPath, "python"));
    }

    [Fact]
    public async Task PrepareForHostRun_uv_missing_with_requirements_warns_and_uses_pip()
    {
        CreateUvProjectFiles();
        File.WriteAllText(Path.Combine(_projectDir.FullName, "requirements.txt"), "azure-functions");
        bool pipRan = false;
        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            GetRuntimeIdentifier = () => DefaultRid,
            FindUv = () => null,
            RunUvSync = (_, _, _, _, _) => throw new InvalidOperationException("uv sync must not run"),
            RunCreateVenv = (_, path, _, _, _) =>
            {
                Directory.CreateDirectory(path);
                return Task.FromResult(0);
            },
            RunPipInstall = (_, _, _, _, _, _) =>
            {
                pipRan = true;
                return Task.FromResult(0);
            },
            ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.12"),
            ReadEnvironmentVariable = _ => null,
        };
        var reporter = new CapturingReporter();
        FunctionsProjectHostRunContext context = CreateContext();
        context.Reporter = reporter;

        await project.PrepareForHostRunAsync(context, default);

        pipRan.Should().BeTrue();
        reporter.Logs.Should().Contain(e => e.Severity == FunctionsProjectReportSeverity.Warning
            && e.Line.Contains("uv was not found") && e.Line.Contains(PythonFunctionsProject.UvInstallUrl));
    }

    [Fact]
    public async Task PrepareForHostRun_uv_missing_without_requirements_throws()
    {
        CreateUvProjectFiles();
        PythonFunctionsProject project = CreateUvProject();
        project.FindUv = () => null;

        GracefulException ex = (await FluentActions.Awaiting(() => project.PrepareForHostRunAsync(CreateContext(), default)).Should().ThrowAsync<GracefulException>()).Which;

        ex.IsUserError.Should().BeTrue();
        ex.Message.Should().Contain("uv was not found on PATH");
        ex.Message.Should().Contain(PythonFunctionsProject.UvInstallUrl);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("[project]\nname = \"app\"\n\n[tool.uv]\nmanaged = false\n", true)]
    [InlineData("[project]\nname = \"app\"\n\n[tool.uv.pip]\nindex-url = \"https://example.test/simple\"\n", false)]
    public async Task PrepareForHostRun_pip_project_with_uv_available_never_calls_uv(string? pyProject, bool hasUvLock)
    {
        File.WriteAllText(Path.Combine(_projectDir.FullName, "requirements.txt"), "azure-functions");
        if (pyProject is not null)
        {
            File.WriteAllText(Path.Combine(_projectDir.FullName, "pyproject.toml"), pyProject);
        }

        if (hasUvLock)
        {
            File.WriteAllText(Path.Combine(_projectDir.FullName, "uv.lock"), "version = 1");
        }

        bool pipRan = false;
        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            GetRuntimeIdentifier = () => DefaultRid,
            FindUv = () => FakeUvPath,
            RunUvSync = (_, _, _, _, _) => throw new InvalidOperationException("uv sync must not run"),
            RunUvExport = (_, _, _, _, _) => throw new InvalidOperationException("uv export must not run"),
            RunCreateVenv = (_, path, _, _, _) =>
            {
                Directory.CreateDirectory(path);
                return Task.FromResult(0);
            },
            RunPipInstall = (_, _, _, _, _, _) =>
            {
                pipRan = true;
                return Task.FromResult(0);
            },
            ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.12"),
            ReadEnvironmentVariable = _ => null,
        };

        await project.PrepareForHostRunAsync(CreateContext(), default);

        pipRan.Should().BeTrue();
    }

    [Fact]
    public async Task PrepareForHostRun_uv_missing_interpreter_throws_with_path()
    {
        CreateUvProjectFiles();
        string emptyEnv = Directory.CreateDirectory(Path.Combine(_projectDir.FullName, "empty-env")).FullName;
        PythonFunctionsProject project = CreateUvProject();
        project.ReadEnvironmentVariable = name => name == PythonFunctionsProject.UvProjectEnvironmentEnvVar ? emptyEnv : null;

        GracefulException ex = (await FluentActions.Awaiting(() => project.PrepareForHostRunAsync(CreateContext(), default)).Should().ThrowAsync<GracefulException>()).Which;

        ex.IsUserError.Should().BeTrue();
        ex.Message.Should().Contain($"No Python interpreter found at {PythonFunctionsProject.GetVenvExecutablePath(emptyEnv, "python")}");
        ex.Message.Should().Contain(PythonFunctionsProject.UvProjectEnvironmentEnvVar);
    }

    [Fact]
    public async Task PrepareForHostRun_python_below_fallback_range_throws_with_pin_hint()
    {
        CreateUvProjectFiles();
        CreateInterpreter(DefaultEnvPath);
        PythonFunctionsProject project = CreateUvProject();
        project.ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.9");

        GracefulException ex = (await FluentActions.Awaiting(() => project.PrepareForHostRunAsync(CreateContext(), default)).Should().ThrowAsync<GracefulException>()).Which;

        ex.IsUserError.Should().BeTrue();
        ex.Message.Should().Contain($"Python 3.9 is not supported by the Azure Functions Python worker on {DefaultRid}");
        ex.Message.Should().Contain("uv python pin 3.13");
    }

    [Fact]
    public async Task PrepareForHostRun_python_unsupported_on_win_arm64_throws_with_platform_pin_hint()
    {
        CreateUvProjectFiles();
        CreateInterpreter(DefaultEnvPath);
        PythonFunctionsProject project = CreateUvProject();
        project.GetRuntimeIdentifier = () => "win-arm64";

        GracefulException ex = (await FluentActions.Awaiting(() => project.PrepareForHostRunAsync(CreateContext(), default)).Should().ThrowAsync<GracefulException>()).Which;

        ex.IsUserError.Should().BeTrue();
        ex.Message.Should().Contain("Python 3.13 is not supported by the Azure Functions Python worker on win-arm64 (supported: 3.15)");
        ex.Message.Should().Contain("uv python pin 3.15");
    }

    [Fact]
    public async Task PrepareForHostRun_python_315_on_win_arm64_is_supported()
    {
        CreateUvProjectFiles();
        CreateInterpreter(DefaultEnvPath);
        PythonFunctionsProject project = CreateUvProject();
        project.GetRuntimeIdentifier = () => "win-arm64";
        project.ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.15");
        FunctionsProjectHostRunContext context = CreateContext();

        await project.PrepareForHostRunAsync(context, default);

        context.EnvironmentVariables[PythonFunctionsProject.WorkerRuntimeVersionEnvVar].Should().Be("3.15");
    }

    [Fact]
    public async Task PrepareForHostRun_worker_payload_versions_override_fallback_table()
    {
        CreateUvProjectFiles();
        CreateInterpreter(DefaultEnvPath);
        string workerDir = CreateWorkerPayload("win-arm64", "3.15");
        PythonFunctionsProject project = CreateUvProject();
        project.GetRuntimeIdentifier = () => "win-arm64";
        project.ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.16");
        FunctionsProjectHostRunContext context = CreateContext();
        context.EnvironmentVariables[PythonFunctionsProject.WorkerDirectoryEnvVar] = workerDir;

        GracefulException ex = (await FluentActions.Awaiting(() => project.PrepareForHostRunAsync(context, default)).Should().ThrowAsync<GracefulException>()).Which;

        // Above the fallback table this would only warn; a readable payload is authoritative.
        ex.Message.Should().Contain("Python 3.16 is not supported").And.Contain("(supported: 3.15)");
    }

    [Fact]
    public async Task PrepareForHostRun_worker_payload_versions_accept_listed_version()
    {
        CreateUvProjectFiles();
        CreateInterpreter(DefaultEnvPath);
        string workerDir = CreateWorkerPayload(DefaultRid, "3.12", "3.17");
        PythonFunctionsProject project = CreateUvProject();
        project.ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.17");
        var reporter = new CapturingReporter();
        FunctionsProjectHostRunContext context = CreateContext();
        context.Reporter = reporter;
        context.EnvironmentVariables[PythonFunctionsProject.WorkerDirectoryEnvVar] = workerDir;

        await project.PrepareForHostRunAsync(context, default);

        context.EnvironmentVariables[PythonFunctionsProject.WorkerRuntimeVersionEnvVar].Should().Be("3.17");
        reporter.Logs.Should().NotContain(e => e.Severity == FunctionsProjectReportSeverity.Warning);
    }

    [Fact]
    public async Task PrepareForHostRun_pip_project_gets_platform_version_check()
    {
        File.WriteAllText(RequirementsPath, "azure-functions\n");
        CreateInterpreter(DefaultEnvPath);
        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            GetRuntimeIdentifier = () => "win-arm64",
            FindUv = () => null,
            RunPipInstall = (_, _, _, _, _, _) => Task.FromResult(0),
            ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.13"),
            ReadEnvironmentVariable = _ => null,
        };

        GracefulException ex = (await FluentActions.Awaiting(() => project.PrepareForHostRunAsync(CreateContext(), default)).Should().ThrowAsync<GracefulException>()).Which;

        ex.Message.Should().Contain("on win-arm64").And.Contain("Recreate the virtual environment");
    }

    [Fact]
    public async Task PrepareForHostRun_python_above_fallback_table_warns_and_continues()
    {
        CreateUvProjectFiles();
        CreateInterpreter(DefaultEnvPath);
        PythonFunctionsProject project = CreateUvProject();
        project.ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.16");
        var reporter = new CapturingReporter();
        FunctionsProjectHostRunContext context = CreateContext();
        context.Reporter = reporter;

        await project.PrepareForHostRunAsync(context, default);

        context.EnvironmentVariables[PythonFunctionsProject.WorkerRuntimeVersionEnvVar].Should().Be("3.16");
        reporter.Logs.Should().Contain(e => e.Severity == FunctionsProjectReportSeverity.Warning && e.Line.Contains("Python 3.16 is newer"));
    }

    [Fact]
    public async Task PrepareForHostRun_python_version_unreadable_warns()
    {
        CreateUvProjectFiles();
        CreateInterpreter(DefaultEnvPath);
        PythonFunctionsProject project = CreateUvProject();
        project.ReadPythonVersion = (_, _) => Task.FromResult<string?>(null);
        var reporter = new CapturingReporter();
        FunctionsProjectHostRunContext context = CreateContext();
        context.Reporter = reporter;

        await project.PrepareForHostRunAsync(context, default);

        context.EnvironmentVariables.ContainsKey(PythonFunctionsProject.WorkerRuntimeVersionEnvVar).Should().BeFalse();
        reporter.Logs.Should().Contain(e => e.Severity == FunctionsProjectReportSeverity.Warning && e.Line.Contains("Could not determine the Python version"));
    }

    [Fact]
    public async Task PrepareForHostRun_supported_python_version_produces_no_warning()
    {
        CreateUvProjectFiles();
        CreateInterpreter(DefaultEnvPath);
        PythonFunctionsProject project = CreateUvProject();
        var reporter = new CapturingReporter();
        FunctionsProjectHostRunContext context = CreateContext();
        context.Reporter = reporter;

        await project.PrepareForHostRunAsync(context, default);

        context.EnvironmentVariables[PythonFunctionsProject.WorkerRuntimeVersionEnvVar].Should().Be("3.13");
        reporter.Logs.Should().NotContain(e => e.Severity == FunctionsProjectReportSeverity.Warning);
    }

    [Fact]
    public async Task PrepareForHostRun_uv_export_writes_missing_requirements()
    {
        CreateUvProjectFiles();
        CreateInterpreter(DefaultEnvPath);
        PythonFunctionsProject project = CreateUvProject(exportOutput: ExportContent);

        await project.PrepareForHostRunAsync(CreateContext(), default);

        File.ReadAllText(RequirementsPath).Should().Be(ExportContent);
    }

    [Fact]
    public async Task PrepareForHostRun_uv_export_overwrites_uv_generated_file_when_changed()
    {
        CreateUvProjectFiles();
        CreateInterpreter(DefaultEnvPath);
        File.WriteAllText(RequirementsPath, PythonFunctionsProject.UvExportHeader + " via the following command:\nold==1.0\n");
        PythonFunctionsProject project = CreateUvProject(exportOutput: ExportContent);

        await project.PrepareForHostRunAsync(CreateContext(), default);

        File.ReadAllText(RequirementsPath).Should().Be(ExportContent);
    }

    [Fact]
    public async Task PrepareForHostRun_uv_export_does_not_rewrite_identical_file()
    {
        CreateUvProjectFiles();
        CreateInterpreter(DefaultEnvPath);
        File.WriteAllText(RequirementsPath, ExportContent);
        DateTime stamp = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(RequirementsPath, stamp);
        PythonFunctionsProject project = CreateUvProject(exportOutput: ExportContent);

        await project.PrepareForHostRunAsync(CreateContext(), default);

        File.GetLastWriteTimeUtc(RequirementsPath).Should().Be(stamp);
    }

    [Fact]
    public async Task PrepareForHostRun_uv_export_leaves_user_authored_file_and_warns()
    {
        CreateUvProjectFiles();
        CreateInterpreter(DefaultEnvPath);
        const string userContent = "# my pins\nazure-functions==1.0\n";
        File.WriteAllText(RequirementsPath, userContent);
        bool exportRan = false;
        PythonFunctionsProject project = CreateUvProject();
        project.RunUvExport = (_, _, _, _, _) =>
        {
            exportRan = true;
            return Task.FromResult((0, ExportContent));
        };
        var reporter = new CapturingReporter();
        FunctionsProjectHostRunContext context = CreateContext();
        context.Reporter = reporter;

        await project.PrepareForHostRunAsync(context, default);

        exportRan.Should().BeFalse();
        File.ReadAllText(RequirementsPath).Should().Be(userContent);
        reporter.Logs.Should().Contain(e => e.Severity == FunctionsProjectReportSeverity.Warning && e.Line.Contains("was not generated by uv"));
    }

    [Fact]
    public async Task PrepareForHostRun_uv_export_failure_warns_and_continues()
    {
        CreateUvProjectFiles();
        CreateInterpreter(DefaultEnvPath);
        PythonFunctionsProject project = CreateUvProject();
        project.RunUvExport = (_, _, _, _, _) => Task.FromResult((2, string.Empty));
        var reporter = new CapturingReporter();
        FunctionsProjectHostRunContext context = CreateContext();
        context.Reporter = reporter;

        await project.PrepareForHostRunAsync(context, default);

        File.Exists(RequirementsPath).Should().BeFalse();
        context.EnvironmentVariables.ContainsKey(PythonFunctionsProject.WorkerExecutablePathEnvVar).Should().BeTrue();
        reporter.Logs.Should().Contain(e => e.Severity == FunctionsProjectReportSeverity.Warning && e.Line.Contains("'uv export' failed (exit 2)"));
    }

    [Fact]
    public async Task PrepareForHostRun_uv_export_requests_non_editable_project_output()
    {
        CreateUvProjectFiles();
        CreateInterpreter(DefaultEnvPath);
        PythonFunctionsProject project = CreateUvProject();
        project.RunUvExport = (_, _, arguments, _, _) =>
        {
            arguments.Should().Contain("--no-editable");
            arguments.Should().NotContain("--no-emit-project");
            return Task.FromResult((0, ExportContent));
        };

        await project.PrepareForHostRunAsync(CreateContext(), default);
    }

    private const string FakeUvPath = "/fake/uv";

    private const string DefaultRid = "linux-x64";

    private const string ExportContent = PythonFunctionsProject.UvExportHeader + " via the following command:\n#    uv export --frozen\nazure-functions==1.24.0\n";

    private string DefaultEnvPath => Path.Combine(_projectDir.FullName, PythonFunctionsProject.DefaultVenvFolderName);

    private string RequirementsPath => Path.Combine(_projectDir.FullName, "requirements.txt");

    private void CreateUvProjectFiles()
    {
        File.WriteAllText(Path.Combine(_projectDir.FullName, "pyproject.toml"), "[project]\nname = \"app\"\n");
        File.WriteAllText(Path.Combine(_projectDir.FullName, "uv.lock"), "version = 1\n");
    }

    private static void CreateInterpreter(string envPath)
    {
        string python = PythonFunctionsProject.GetVenvExecutablePath(envPath, "python");
        Directory.CreateDirectory(Path.GetDirectoryName(python)!);
        File.WriteAllText(python, string.Empty);
    }

    // Lays out `<version>/<os>/<architecture>/worker.py` the way the host expects, plus a decoy folder.
    private string CreateWorkerPayload(string rid, params string[] versions)
    {
        string workerDir = Directory.CreateDirectory(Path.Combine(_projectDir.FullName, "worker")).FullName;
        (string os, string architecture) = PythonSupportedVersions.GetWorkerPlatform(rid);
        foreach (string version in versions)
        {
            string dir = Directory.CreateDirectory(Path.Combine(workerDir, version, os, architecture)).FullName;
            File.WriteAllText(Path.Combine(dir, "worker.py"), string.Empty);
        }

        Directory.CreateDirectory(Path.Combine(workerDir, "3.11", "OTHER", "X64"));
        return workerDir;
    }

    // uv-mode project whose pip/venv seams fail the test if invoked.
    private PythonFunctionsProject CreateUvProject(string exportOutput = "")
        => new(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            GetRuntimeIdentifier = () => DefaultRid,
            FindUv = () => FakeUvPath,
            RunUvSync = (_, _, _, _, _) => Task.FromResult(0),
            RunUvExport = (_, _, _, _, _) => Task.FromResult((0, exportOutput)),
            RunCreateVenv = (_, _, _, _, _) => throw new InvalidOperationException("venv must not be created for uv projects"),
            RunPipInstall = (_, _, _, _, _, _) => throw new InvalidOperationException("pip must not run for uv projects"),
            ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.13"),
            ReadEnvironmentVariable = _ => null,
        };

    private FunctionsProjectHostRunContext CreateContext()
        => new(_projectDir, "python", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FUNCTIONS_WORKER_RUNTIME"] = "python",
        });

    private sealed class CapturingReporter : IFunctionsProjectHostRunReporter
    {
        private readonly List<(string Line, FunctionsProjectReportSeverity Severity)> _logs = [];
        private readonly object _gate = new();

        public IReadOnlyList<(string Line, FunctionsProjectReportSeverity Severity)> Logs
        {
            get
            {
                lock (_gate)
                {
                    return _logs.ToArray();
                }
            }
        }

        public void ReportStatus(string message)
        {
        }

        public void WriteLog(string line, FunctionsProjectReportSeverity severity = FunctionsProjectReportSeverity.Info)
        {
            lock (_gate)
            {
                _logs.Add((line, severity));
            }
        }
    }
}
