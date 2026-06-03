// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Xunit;

namespace Azure.Functions.Cli.Workloads.Node.Tests;

public class NodeFunctionsProjectTests : IDisposable
{
    private readonly DirectoryInfo _projectDir;

    public NodeFunctionsProjectTests()
    {
        _projectDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "func-node-project-" + Guid.NewGuid().ToString("N")));
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
    public async Task PrepareForHostRun_without_package_json_is_noop()
    {
        bool invoked = false;
        NodeFunctionsProject project = CreateProject((_, _, _, _, _) =>
        {
            invoked = true;
            return Task.FromResult(0);
        });

        await project.PrepareForHostRunAsync(CreateContext(), default);

        Assert.False(invoked);
    }

    [Fact]
    public async Task PrepareForHostRun_runs_npm_install_when_node_modules_missing()
    {
        WritePackageJson("""{ "name": "x", "version": "1.0.0" }""");
        var commands = new List<IReadOnlyList<string>>();
        NodeFunctionsProject project = CreateProject((_, args, _, _, _) =>
        {
            commands.Add(args);
            return Task.FromResult(0);
        });

        await project.PrepareForHostRunAsync(CreateContext(), default);

        Assert.Single(commands);
        Assert.Equal(new[] { "install" }, commands[0]);
    }

    [Fact]
    public async Task PrepareForHostRun_skips_install_when_node_modules_present()
    {
        WritePackageJson("""{ "name": "x", "version": "1.0.0" }""");
        Directory.CreateDirectory(Path.Combine(_projectDir.FullName, "node_modules"));
        var commands = new List<IReadOnlyList<string>>();
        NodeFunctionsProject project = CreateProject((_, args, _, _, _) =>
        {
            commands.Add(args);
            return Task.FromResult(0);
        });

        await project.PrepareForHostRunAsync(CreateContext(), default);

        Assert.Empty(commands);
    }

    [Fact]
    public async Task PrepareForHostRun_runs_build_script_when_declared()
    {
        WritePackageJson("""{ "name": "x", "scripts": { "build": "tsc" } }""");
        Directory.CreateDirectory(Path.Combine(_projectDir.FullName, "node_modules"));
        var commands = new List<IReadOnlyList<string>>();
        NodeFunctionsProject project = CreateProject((_, args, _, _, _) =>
        {
            commands.Add(args);
            return Task.FromResult(0);
        });

        await project.PrepareForHostRunAsync(CreateContext(), default);

        Assert.Single(commands);
        Assert.Equal(new[] { "run", "build" }, commands[0]);
    }

    [Fact]
    public async Task PrepareForHostRun_streams_output_to_reporter()
    {
        WritePackageJson("""{ "name": "x", "version": "1.0.0" }""");
        NodeFunctionsProject project = CreateProject((_, _, onOut, onErr, _) =>
        {
            onOut("added 42 packages");
            onErr("npm warn deprecated");
            return Task.FromResult(0);
        });
        var reporter = new CapturingReporter();
        FunctionsProjectHostRunContext context = CreateContext();
        context.Reporter = reporter;

        await project.PrepareForHostRunAsync(context, default);

        Assert.Contains(reporter.Logs, e => e.Severity == FunctionsProjectReportSeverity.Info && e.Line == "added 42 packages");
        Assert.Contains(reporter.Logs, e => e.Severity == FunctionsProjectReportSeverity.Error && e.Line == "npm warn deprecated");
    }

    [Fact]
    public async Task PrepareForHostRun_throws_graceful_on_install_failure()
    {
        WritePackageJson("""{ "name": "x" }""");
        NodeFunctionsProject project = CreateProject((_, _, _, onErr, _) =>
        {
            onErr("ENOENT");
            return Task.FromResult(1);
        });
        var reporter = new CapturingReporter();
        FunctionsProjectHostRunContext context = CreateContext();
        context.Reporter = reporter;

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => project.PrepareForHostRunAsync(context, default));

        Assert.True(ex.IsUserError);
        Assert.Contains("install", ex.Message);
        Assert.Contains("exit 1", ex.Message);
        Assert.Contains(reporter.Logs, e => e.Severity == FunctionsProjectReportSeverity.Error && e.Line == "ENOENT");
    }

    [Fact]
    public async Task PrepareForHostRun_skips_build_script_when_SkipBuild()
    {
        WritePackageJson("""{ "name": "x", "scripts": { "build": "tsc" } }""");
        Directory.CreateDirectory(Path.Combine(_projectDir.FullName, "node_modules"));
        var commands = new List<IReadOnlyList<string>>();
        NodeFunctionsProject project = CreateProject((_, args, _, _, _) =>
        {
            commands.Add(args);
            return Task.FromResult(0);
        });

        await project.PrepareForHostRunAsync(CreateContext(skipBuild: true), default);

        Assert.Empty(commands);
    }

    [Fact]
    public async Task PrepareForHostRun_still_runs_install_when_SkipBuild()
    {
        WritePackageJson("""{ "name": "x", "scripts": { "build": "tsc" } }""");
        var commands = new List<IReadOnlyList<string>>();
        NodeFunctionsProject project = CreateProject((_, args, _, _, _) =>
        {
            commands.Add(args);
            return Task.FromResult(0);
        });

        await project.PrepareForHostRunAsync(CreateContext(skipBuild: true), default);

        Assert.Single(commands);
        Assert.Equal(new[] { "install" }, commands[0]);
    }

    [Fact]
    public void Language_ReturnsTypeScript_WhenTsconfigPresent()
    {
        File.WriteAllText(Path.Combine(_projectDir.FullName, "tsconfig.json"), "{}");
        File.WriteAllText(Path.Combine(_projectDir.FullName, "package.json"), "{}");
        File.WriteAllText(Path.Combine(_projectDir.FullName, "index.js"), string.Empty);

        var project = new NodeFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName));

        Assert.Equal("TypeScript", project.Language);
    }

    [Fact]
    public void Language_ReturnsTypeScript_WhenTopLevelTsFilePresent()
    {
        File.WriteAllText(Path.Combine(_projectDir.FullName, "index.ts"), string.Empty);

        var project = new NodeFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName));

        Assert.Equal("TypeScript", project.Language);
    }

    [Fact]
    public void Language_ReturnsJavaScript_WhenOnlyPackageJsonPresent()
    {
        File.WriteAllText(Path.Combine(_projectDir.FullName, "package.json"), "{}");

        var project = new NodeFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName));

        Assert.Equal("JavaScript", project.Language);
    }

    [Fact]
    public void Language_DefaultsToJavaScript_WhenNoFingerprint()
    {
        // The factory will only construct a NodeFunctionsProject after it has
        // already classified the directory as Node, so falling back to JS when
        // there's no further signal is safe.
        var project = new NodeFunctionsProject(WorkingDirectory.FromExplicit(_projectDir.FullName));

        Assert.Equal("JavaScript", project.Language);
    }

    private NodeFunctionsProject CreateProject(Func<string, IReadOnlyList<string>, Action<string>, Action<string>, CancellationToken, Task<int>> runner)
        => new(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            RunNpm = runner,
        };

    private void WritePackageJson(string contents)
        => File.WriteAllText(Path.Combine(_projectDir.FullName, "package.json"), contents);

    private FunctionsProjectHostRunContext CreateContext(bool skipBuild = false)
        => new(_projectDir, "node", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
        }, skipBuild);

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
