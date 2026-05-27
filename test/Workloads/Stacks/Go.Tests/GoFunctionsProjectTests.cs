// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Xunit;

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
        GoFunctionsProject project = CreateProject((root, output, _) =>
        {
            observedRoot = root;
            observedOutput = output;
            return Task.FromResult((0, string.Empty));
        });

        await project.PrepareForHostRunAsync(CreateContext(), default);

        string expectedName = OperatingSystem.IsWindows() ? "app.exe" : "app";
        Assert.Equal(_projectDir.FullName, observedRoot);
        Assert.Equal(Path.Combine(_projectDir.FullName, "bin", expectedName), observedOutput);
    }

    [Fact]
    public async Task PrepareForHostRun_throws_graceful_on_nonzero_exit()
    {
        GoFunctionsProject project = CreateProject((_, _, _) => Task.FromResult((1, "compile error")));

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => project.PrepareForHostRunAsync(CreateContext(), default));

        Assert.True(ex.IsUserError);
        Assert.Contains("go build", ex.Message);
        Assert.Contains("compile error", ex.Message);
    }

    [Fact]
    public async Task PrepareForHostRun_skips_build_when_SkipBuild()
    {
        bool invoked = false;
        GoFunctionsProject project = CreateProject((_, _, _) =>
        {
            invoked = true;
            return Task.FromResult((0, string.Empty));
        });

        await project.PrepareForHostRunAsync(CreateContext(skipBuild: true), default);

        Assert.False(invoked);
    }

    [Fact]
    public async Task PrepareForHostRun_throws_graceful_when_go_not_installed()
    {
        GoFunctionsProject project = CreateProject((_, _, _) => Task.FromResult((0, string.Empty)));
        project.ReadGoVersion = _ => Task.FromResult<(int, int)?>(null);

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => project.PrepareForHostRunAsync(CreateContext(), default));

        Assert.True(ex.IsUserError);
        Assert.Contains("Could not find a Go installation", ex.Message);
    }

    [Fact]
    public async Task PrepareForHostRun_throws_graceful_when_go_too_old()
    {
        GoFunctionsProject project = CreateProject((_, _, _) => Task.FromResult((0, string.Empty)));
        project.ReadGoVersion = _ => Task.FromResult<(int, int)?>((1, 21));

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => project.PrepareForHostRunAsync(CreateContext(), default));

        Assert.True(ex.IsUserError);
        Assert.Contains("Go 1.21 is not supported", ex.Message);
    }

    private GoFunctionsProject CreateProject(Func<string, string, CancellationToken, Task<(int, string)>> runner)
        => new(WorkingDirectory.FromExplicit(_projectDir.FullName))
        {
            RunGoBuild = runner,
            ReadGoVersion = _ => Task.FromResult<(int, int)?>((1, 24)),
        };

    private FunctionsProjectHostRunContext CreateContext(bool skipBuild = false)
        => new(_projectDir, "go", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), skipBuild);
}
