// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Go.Tests;

public class GoFunctionsProjectTests : IDisposable
{
    private readonly DirectoryInfo _projectDir;

    public GoFunctionsProjectTests()
    {
        _projectDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "func-go-project-" + Guid.NewGuid().ToString("N")));
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
    public async Task PrepareForHostRun_invokes_go_build_to_bin_app()
    {
        File.WriteAllText(Path.Combine(_projectDir.FullName, "go.mod"), "module example.com/myapp\n\ngo 1.24\n");

        string? observedRoot = null;
        string? observedOutput = null;
        GoFunctionsProject project = CreateProject((root, output, _, _, _) =>
        {
            observedRoot = root;
            observedOutput = output;
            return Task.FromResult(0);
        });

        FunctionsProjectHostRunContext context = CreateContext();
        await project.PrepareForHostRunAsync(context, default);

        string expectedName = OperatingSystem.IsWindows() ? "app.exe" : "app";
        observedRoot.Should().Be(_projectDir.FullName);
        observedOutput.Should().Be(Path.Combine(_projectDir.FullName, "bin", expectedName));
        // StartupDirectory must remain the project root so the host finds host.json
        // and the worker's `defaultExecutablePath = bin/app` resolves correctly.
        context.StartupDirectory.FullName.Should().Be(_projectDir.FullName);
    }

    [Fact]
    public async Task PrepareForHostRun_streams_build_output_to_reporter()
    {
        File.WriteAllText(Path.Combine(_projectDir.FullName, "go.mod"), "module example.com/myapp\n\ngo 1.24\n");

        GoFunctionsProject project = CreateProject((_, _, onOut, onErr, _) =>
        {
            onOut("compiling example.com/myapp");
            onErr("go: warning");
            return Task.FromResult(0);
        });
        var reporter = new CapturingReporter();
        FunctionsProjectHostRunContext context = CreateContext();
        context.Reporter = reporter;

        await project.PrepareForHostRunAsync(context, default);

        reporter.Logs.Should().Contain(e => e.Severity == FunctionsProjectReportSeverity.Info && e.Line == "compiling example.com/myapp");
        reporter.Logs.Should().Contain(e => e.Severity == FunctionsProjectReportSeverity.Error && e.Line == "go: warning");
    }

    [Fact]
    public async Task PrepareForHostRun_throws_graceful_on_nonzero_exit()
    {
        GoFunctionsProject project = CreateProject((_, _, _, onErr, _) =>
        {
            onErr("compile error");
            return Task.FromResult(1);
        });
        var reporter = new CapturingReporter();
        FunctionsProjectHostRunContext context = CreateContext();
        context.Reporter = reporter;

        GracefulException ex = (await FluentActions.Awaiting(() => project.PrepareForHostRunAsync(context, default)).Should().ThrowAsync<GracefulException>()).Which;

        ex.IsUserError.Should().BeTrue();
        ex.Message.Should().Contain("go build");
        ex.Message.Should().Contain("exit 1");
        reporter.Logs.Should().Contain(e => e.Severity == FunctionsProjectReportSeverity.Error && e.Line == "compile error");
    }

    [Fact]
    public async Task PrepareForHostRun_skips_build_when_SkipBuild()
    {
        bool invoked = false;
        GoFunctionsProject project = CreateProject((_, _, _, _, _) =>
        {
            invoked = true;
            return Task.FromResult(0);
        });

        FunctionsProjectHostRunContext context = CreateContext(skipBuild: true);
        await project.PrepareForHostRunAsync(context, default);

        invoked.Should().BeFalse();
        context.StartupDirectory.FullName.Should().Be(_projectDir.FullName);
    }

    [Fact]
    public async Task PrepareForHostRun_throws_graceful_when_go_not_installed()
    {
        GoFunctionsProject project = CreateProject((_, _, _, _, _) => Task.FromResult(0));
        project.ReadGoVersion = _ => Task.FromResult<(int, int)?>(null);

        GracefulException ex = (await FluentActions.Awaiting(() => project.PrepareForHostRunAsync(CreateContext(), default)).Should().ThrowAsync<GracefulException>()).Which;

        ex.IsUserError.Should().BeTrue();
        ex.Message.Should().Contain("Could not find a Go installation");
    }

    [Fact]
    public async Task PrepareForHostRun_throws_graceful_when_go_too_old()
    {
        GoFunctionsProject project = CreateProject((_, _, _, _, _) => Task.FromResult(0));
        project.ReadGoVersion = _ => Task.FromResult<(int, int)?>((1, 21));

        GracefulException ex = (await FluentActions.Awaiting(() => project.PrepareForHostRunAsync(CreateContext(), default)).Should().ThrowAsync<GracefulException>()).Which;

        ex.IsUserError.Should().BeTrue();
        ex.Message.Should().Contain("Go 1.21 is not supported");
    }

    [Fact]
    public async Task PrepareForHostRun_runs_go_mod_tidy_before_building()
    {
        File.WriteAllText(Path.Combine(_projectDir.FullName, "go.mod"), "module example.com/myapp\n\ngo 1.24\n");

        var calls = new List<string>();
        GoFunctionsProject project = CreateProject((_, _, _, _, _) =>
        {
            calls.Add("build");
            return Task.FromResult(0);
        });
        project.RunGoModTidy = (root, _, _, _) =>
        {
            calls.Add($"tidy:{root}");
            return Task.FromResult(0);
        };

        await project.PrepareForHostRunAsync(CreateContext(), default);

        calls.Count.Should().Be(2);
        calls[0].Should().Be($"tidy:{_projectDir.FullName}");
        calls[1].Should().Be("build");
    }

    [Fact]
    public async Task PrepareForHostRun_throws_graceful_when_tidy_fails()
    {
        GoFunctionsProject project = CreateProject((_, _, _, _, _) => Task.FromResult(0));
        project.RunGoModTidy = (_, _, onErr, _) =>
        {
            onErr("module not found");
            return Task.FromResult(1);
        };
        var reporter = new CapturingReporter();
        FunctionsProjectHostRunContext context = CreateContext();
        context.Reporter = reporter;

        GracefulException ex = (await FluentActions.Awaiting(() => project.PrepareForHostRunAsync(context, default)).Should().ThrowAsync<GracefulException>()).Which;

        ex.IsUserError.Should().BeTrue();
        ex.Message.Should().Contain("go mod tidy");
        ex.Message.Should().Contain("exit 1");
        reporter.Logs.Should().Contain(e => e.Severity == FunctionsProjectReportSeverity.Error && e.Line == "module not found");
    }

    [Fact]
    public async Task PrepareForHostRun_skips_tidy_when_SkipBuild()
    {
        bool tidyInvoked = false;
        GoFunctionsProject project = CreateProject((_, _, _, _, _) => Task.FromResult(0));
        project.RunGoModTidy = (_, _, _, _) =>
        {
            tidyInvoked = true;
            return Task.FromResult(0);
        };

        await project.PrepareForHostRunAsync(CreateContext(skipBuild: true), default);

        tidyInvoked.Should().BeFalse();
    }

    private GoFunctionsProject CreateProject(Func<string, string, Action<string>, Action<string>, CancellationToken, Task<int>> runner)
        => new(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            RunGoBuild = runner,
            ReadGoVersion = _ => Task.FromResult<(int, int)?>((1, 24)),
            RunGoModTidy = (_, _, _, _) => Task.FromResult(0),
        };

    private FunctionsProjectHostRunContext CreateContext(bool skipBuild = false)
        => new(_projectDir, "go", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), skipBuild);

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
