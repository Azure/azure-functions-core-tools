// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Xunit;

namespace Azure.Functions.Cli.Workloads.Java.Tests;

public class JavaFunctionsProjectTests : IDisposable
{
    private readonly DirectoryInfo _projectDir;

    public JavaFunctionsProjectTests()
    {
        _projectDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "func-java-project-" + Guid.NewGuid().ToString("N")));
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
    public async Task PrepareForHostRun_builds_with_maven_and_points_startup_at_staged_app()
    {
        string? observedRoot = null;
        JavaFunctionsProject project = CreateProject((root, _, _, _, _) =>
        {
            observedRoot = root;
            CreateStagedApp();
            return Task.FromResult(0);
        });

        FunctionsProjectHostRunContext context = CreateContext();
        await project.PrepareForHostRunAsync(context, default);

        Assert.Equal(_projectDir.FullName, observedRoot);
        Assert.Equal(
            Path.Combine(_projectDir.FullName, "target", "azure-functions", "app"),
            context.StartupDirectory.FullName);
    }

    [Fact]
    public async Task PrepareForHostRun_streams_build_output_to_reporter()
    {
        JavaFunctionsProject project = CreateProject((_, _, onOut, onErr, _) =>
        {
            onOut("[INFO] BUILD SUCCESS");
            onErr("[WARNING] something");
            CreateStagedApp();
            return Task.FromResult(0);
        });
        var reporter = new CapturingReporter();
        FunctionsProjectHostRunContext context = CreateContext();
        context.Reporter = reporter;

        await project.PrepareForHostRunAsync(context, default);

        Assert.Contains(reporter.Logs, e => e.Severity == FunctionsProjectReportSeverity.Info && e.Line == "[INFO] BUILD SUCCESS");
        Assert.Contains(reporter.Logs, e => e.Severity == FunctionsProjectReportSeverity.Error && e.Line == "[WARNING] something");
    }

    [Fact]
    public async Task PrepareForHostRun_throws_graceful_on_nonzero_exit()
    {
        JavaFunctionsProject project = CreateProject((_, _, _, onErr, _) =>
        {
            onErr("[ERROR] BUILD FAILURE");
            return Task.FromResult(1);
        });
        var reporter = new CapturingReporter();
        FunctionsProjectHostRunContext context = CreateContext();
        context.Reporter = reporter;

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => project.PrepareForHostRunAsync(context, default));

        Assert.True(ex.IsUserError);
        Assert.Contains("mvn clean package", ex.Message);
        Assert.Contains("exit 1", ex.Message);
        Assert.Contains(reporter.Logs, e => e.Severity == FunctionsProjectReportSeverity.Error && e.Line == "[ERROR] BUILD FAILURE");
    }

    [Fact]
    public async Task PrepareForHostRun_skips_build_when_SkipBuild_and_uses_existing_staged_app()
    {
        CreateStagedApp();
        bool invoked = false;
        JavaFunctionsProject project = CreateProject((_, _, _, _, _) =>
        {
            invoked = true;
            return Task.FromResult(0);
        });

        FunctionsProjectHostRunContext context = CreateContext(skipBuild: true);
        await project.PrepareForHostRunAsync(context, default);

        Assert.False(invoked);
        Assert.Equal(
            Path.Combine(_projectDir.FullName, "target", "azure-functions", "app"),
            context.StartupDirectory.FullName);
    }

    [Fact]
    public async Task PrepareForHostRun_throws_graceful_when_maven_not_found()
    {
        JavaFunctionsProject project = CreateProject((_, _, _, _, _) => Task.FromResult(0));
        project.ResolveMavenCommand = (_, _) => Task.FromResult<string?>(null);

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => project.PrepareForHostRunAsync(CreateContext(), default));

        Assert.True(ex.IsUserError);
        Assert.Contains("Could not find Apache Maven", ex.Message);
    }

    [Fact]
    public async Task PrepareForHostRun_throws_graceful_when_no_staged_app_produced()
    {
        // Build "succeeds" but produces no staged app under target/azure-functions.
        JavaFunctionsProject project = CreateProject((_, _, _, _, _) => Task.FromResult(0));

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => project.PrepareForHostRunAsync(CreateContext(), default));

        Assert.True(ex.IsUserError);
        Assert.Contains("No built Functions app", ex.Message);
    }

    [Theory]
    [InlineData("mvn.cmd", true)]
    [InlineData("mvnw.CMD", true)]
    [InlineData("build.bat", true)]
    [InlineData("mvn", false)]
    [InlineData("go", false)]
    public void IsBatchScript_DetectsWindowsBatchLaunchers(string fileName, bool expected)
    {
        Assert.Equal(expected, JavaFunctionsProject.IsBatchScript(fileName));
    }

    private DirectoryInfo CreateStagedApp(string appName = "app")
    {
        DirectoryInfo dir = Directory.CreateDirectory(
            Path.Combine(_projectDir.FullName, "target", "azure-functions", appName));
        File.WriteAllText(Path.Combine(dir.FullName, "host.json"), "{}");
        return dir;
    }

    private JavaFunctionsProject CreateProject(Func<string, string, Action<string>, Action<string>, CancellationToken, Task<int>> runner)
        => new(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            ResolveMavenCommand = (_, _) => Task.FromResult<string?>("mvn"),
            RunMavenPackage = runner,
        };

    private FunctionsProjectHostRunContext CreateContext(bool skipBuild = false)
        => new(_projectDir, "java", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), skipBuild);

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
