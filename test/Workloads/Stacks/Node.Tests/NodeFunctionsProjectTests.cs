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
        NodeFunctionsProject project = CreateProject((_, _, _) =>
        {
            invoked = true;
            return Task.FromResult((0, string.Empty));
        });

        await project.PrepareForHostRunAsync(CreateContext(), default);

        Assert.False(invoked);
    }

    [Fact]
    public async Task PrepareForHostRun_runs_npm_install_when_node_modules_missing()
    {
        WritePackageJson("""{ "name": "x", "version": "1.0.0" }""");
        var commands = new List<IReadOnlyList<string>>();
        NodeFunctionsProject project = CreateProject((_, args, _) =>
        {
            commands.Add(args);
            return Task.FromResult((0, string.Empty));
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
        NodeFunctionsProject project = CreateProject((_, args, _) =>
        {
            commands.Add(args);
            return Task.FromResult((0, string.Empty));
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
        NodeFunctionsProject project = CreateProject((_, args, _) =>
        {
            commands.Add(args);
            return Task.FromResult((0, string.Empty));
        });

        await project.PrepareForHostRunAsync(CreateContext(), default);

        Assert.Single(commands);
        Assert.Equal(new[] { "run", "build" }, commands[0]);
    }

    [Fact]
    public async Task PrepareForHostRun_throws_graceful_on_install_failure()
    {
        WritePackageJson("""{ "name": "x" }""");
        NodeFunctionsProject project = CreateProject((_, _, _) => Task.FromResult((1, "ENOENT")));

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => project.PrepareForHostRunAsync(CreateContext(), default));

        Assert.True(ex.IsUserError);
        Assert.Contains("npm install", ex.Message);
        Assert.Contains("ENOENT", ex.Message);
    }

    [Fact]
    public async Task PrepareForHostRun_skips_build_script_when_SkipBuild()
    {
        WritePackageJson("""{ "name": "x", "scripts": { "build": "tsc" } }""");
        Directory.CreateDirectory(Path.Combine(_projectDir.FullName, "node_modules"));
        var commands = new List<IReadOnlyList<string>>();
        NodeFunctionsProject project = CreateProject((_, args, _) =>
        {
            commands.Add(args);
            return Task.FromResult((0, string.Empty));
        });

        await project.PrepareForHostRunAsync(CreateContext(skipBuild: true), default);

        Assert.Empty(commands);
    }

    [Fact]
    public async Task PrepareForHostRun_still_runs_install_when_SkipBuild()
    {
        WritePackageJson("""{ "name": "x", "scripts": { "build": "tsc" } }""");
        var commands = new List<IReadOnlyList<string>>();
        NodeFunctionsProject project = CreateProject((_, args, _) =>
        {
            commands.Add(args);
            return Task.FromResult((0, string.Empty));
        });

        await project.PrepareForHostRunAsync(CreateContext(skipBuild: true), default);

        Assert.Single(commands);
        Assert.Equal(new[] { "install" }, commands[0]);
    }

    private NodeFunctionsProject CreateProject(Func<string, IReadOnlyList<string>, CancellationToken, Task<(int, string)>> runner)
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
}
