// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public class DotNetSourceProjectTests : IDisposable
{
    private readonly DirectoryInfo _projectDir;
    private readonly IDotnetCliRunner _dotnetCli = Substitute.For<IDotnetCliRunner>();

    public DotNetSourceProjectTests()
    {
        _projectDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "func-dotnet-source-" + Guid.NewGuid().ToString("N")));
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
    public async Task PrepareForHostRunAsync_builds_then_sets_startup_directory()
    {
        string projectFile = Path.Combine(_projectDir.FullName, "MyApp.csproj");
        File.WriteAllText(projectFile, "<Project></Project>");

        _dotnetCli.RunWithOutputAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("bin/Debug/net10.0/\n");

        DotNetSourceProject project = CreateProject(projectFile);
        FunctionsProjectHostRunContext context = CreateHostRunContext();

        await project.PrepareForHostRunAsync(context, default);

        // Build should be called before output path query (verified by call order)
        Received.InOrder(() =>
        {
            _dotnetCli.RunAsync(
                Arg.Is<IReadOnlyList<string>>(args => args[0] == "build" && args[1] == projectFile),
                _projectDir.FullName,
                Arg.Any<CancellationToken>());
            _dotnetCli.RunWithOutputAsync(
                Arg.Is<IReadOnlyList<string>>(args => args[0] == "msbuild" && args[2] == "--getProperty:OutputPath"),
                _projectDir.FullName,
                Arg.Any<CancellationToken>());
        });

        string expectedPath = Path.GetFullPath("bin/Debug/net10.0/", _projectDir.FullName);
        Assert.Equal(expectedPath, context.StartupDirectory.FullName);
    }

    [Fact]
    public async Task PrepareForHostRunAsync_handles_absolute_output_path()
    {
        string projectFile = Path.Combine(_projectDir.FullName, "MyApp.csproj");
        File.WriteAllText(projectFile, "<Project></Project>");

        string absoluteOutput = Path.Combine(_projectDir.FullName, "custom-output");
        _dotnetCli.RunWithOutputAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(absoluteOutput + "\n");

        DotNetSourceProject project = CreateProject(projectFile);
        FunctionsProjectHostRunContext context = CreateHostRunContext();

        await project.PrepareForHostRunAsync(context, default);

        Assert.Equal(absoluteOutput, context.StartupDirectory.FullName);
    }

    [Fact]
    public async Task PrepareForHostRunAsync_throws_when_output_path_is_empty()
    {
        string projectFile = Path.Combine(_projectDir.FullName, "MyApp.csproj");
        File.WriteAllText(projectFile, "<Project></Project>");

        _dotnetCli.RunWithOutputAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("   \n");

        DotNetSourceProject project = CreateProject(projectFile);
        FunctionsProjectHostRunContext context = CreateHostRunContext();

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => project.PrepareForHostRunAsync(context, default));

        Assert.Contains("OutputPath", ex.Message);
        Assert.True(ex.IsUserError);
    }

    [Fact]
    public async Task PrepareForHostRunAsync_propagates_build_failure()
    {
        string projectFile = Path.Combine(_projectDir.FullName, "MyApp.csproj");
        File.WriteAllText(projectFile, "<Project></Project>");

        _dotnetCli.RunAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new DotnetCliException(1, "Build failed", "", "build MyApp.csproj")));

        DotNetSourceProject project = CreateProject(projectFile);
        FunctionsProjectHostRunContext context = CreateHostRunContext();

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => project.PrepareForHostRunAsync(context, default));

        Assert.Contains("dotnet build", ex.Message);
        Assert.Contains("exit 1", ex.Message);
        Assert.True(ex.IsUserError);
    }

    [Fact]
    public async Task PrepareForHostRunAsync_throws_on_null_context()
    {
        string projectFile = Path.Combine(_projectDir.FullName, "MyApp.csproj");
        File.WriteAllText(projectFile, "<Project></Project>");

        DotNetSourceProject project = CreateProject(projectFile);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => project.PrepareForHostRunAsync(null!, default));
    }

    [Fact]
    public async Task PrepareForHostRunAsync_respects_cancellation()
    {
        string projectFile = Path.Combine(_projectDir.FullName, "MyApp.csproj");
        File.WriteAllText(projectFile, "<Project></Project>");

        DotNetSourceProject project = CreateProject(projectFile);
        FunctionsProjectHostRunContext context = CreateHostRunContext();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => project.PrepareForHostRunAsync(context, cts.Token));
    }

    [Fact]
    public async Task PrepareForHostRunAsync_skips_build_when_skip_build_is_true()
    {
        string projectFile = Path.Combine(_projectDir.FullName, "MyApp.csproj");
        File.WriteAllText(projectFile, "<Project></Project>");

        _dotnetCli.RunWithOutputAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("bin/Debug/net10.0/\n");

        DotNetSourceProject project = CreateProject(projectFile);
        FunctionsProjectHostRunContext context = CreateHostRunContext(skipBuild: true);

        await project.PrepareForHostRunAsync(context, default);

        // Build should NOT be called
        await _dotnetCli.DidNotReceive().RunAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());

        // OutputPath query should still be called
        await _dotnetCli.Received(1).RunWithOutputAsync(
            Arg.Is<IReadOnlyList<string>>(args => args[0] == "msbuild" && args[2] == "--getProperty:OutputPath"),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());

        string expectedPath = Path.GetFullPath("bin/Debug/net10.0/", _projectDir.FullName);
        Assert.Equal(expectedPath, context.StartupDirectory.FullName);
    }

    private DotNetSourceProject CreateProject(string projectFile)
        => new(WorkingDirectory.FromExplicit(_projectDir.FullName), projectFile, _dotnetCli);

    private FunctionsProjectHostRunContext CreateHostRunContext(bool skipBuild = false)
        => new(_projectDir, "dotnet", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), skipBuild);
}
