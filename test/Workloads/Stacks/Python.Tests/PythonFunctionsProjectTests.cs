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
    public async Task PrepareForHostRun_streams_venv_output_to_reporter()
    {
        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
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

    [Fact]
    public async Task PrepareForHostRun_sets_worker_executable_path_and_runtime_version()
    {
        File.WriteAllText(Path.Combine(_projectDir.FullName, "requirements.txt"), "azure-functions");
        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            RunCreateVenv = (_, path, _, _, _) =>
            {
                Directory.CreateDirectory(path);
                return Task.FromResult(0);
            },
            RunPipInstall = (_, _, _, _, _, _) => Task.FromResult(0),
            ReadPythonVersion = (_, _) => Task.FromResult<string?>("3.12"),
            ReadEnvironmentVariable = _ => null,
        };
        FunctionsProjectHostRunContext context = CreateContext();

        await project.PrepareForHostRunAsync(context, default);

        string expectedExe = PythonFunctionsProject.GetVenvExecutablePath(
            Path.Combine(_projectDir.FullName, PythonFunctionsProject.DefaultVenvFolderName),
            "python");

        context.EnvironmentVariables[PythonFunctionsProject.WorkerExecutablePathEnvVar].Should().Be(expectedExe);
        context.EnvironmentVariables[PythonFunctionsProject.WorkerRuntimeVersionEnvVar].Should().Be("3.12");
    }

    [Fact]
    public async Task PrepareForHostRun_omits_runtime_version_when_python_version_unreadable()
    {
        var project = new PythonFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
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
