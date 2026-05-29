// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
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
    public async Task PrepareForHostRunAsync_builds_and_sets_startup_directory_from_target_result()
    {
        string projectFile = Path.Combine(_projectDir.FullName, "MyApp.csproj");
        File.WriteAllText(projectFile, "<Project></Project>");

        string assemblyPath = Path.Combine(_projectDir.FullName, "bin", "Debug", "net10.0", "MyApp.dll");
        string json = BuildTargetResultJson(assemblyPath, "Build");

        _dotnetCli.RunWithOutputAsync(
                Arg.Is<IReadOnlyList<string>>(args => args[0] == "build" && args.Contains("--getTargetResult:Build")),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(json);

        DotNetSourceProject project = CreateProject(projectFile);
        FunctionsProjectHostRunContext context = CreateHostRunContext();

        await project.PrepareForHostRunAsync(context, default);

        string expectedDir = Path.GetDirectoryName(assemblyPath)!;
        Assert.Equal(expectedDir, context.StartupDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public async Task PrepareForHostRunAsync_skip_build_uses_get_target_path()
    {
        string projectFile = Path.Combine(_projectDir.FullName, "MyApp.csproj");
        File.WriteAllText(projectFile, "<Project></Project>");

        string assemblyPath = Path.Combine(_projectDir.FullName, "bin", "Debug", "net10.0", "MyApp.dll");
        string json = BuildTargetResultJson(assemblyPath, "GetTargetPath");

        _dotnetCli.RunWithOutputAsync(
                Arg.Is<IReadOnlyList<string>>(args => args.Contains("--getTargetResult:GetTargetPath")),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(json);

        DotNetSourceProject project = CreateProject(projectFile);
        FunctionsProjectHostRunContext context = CreateHostRunContext(skipBuild: true);

        await project.PrepareForHostRunAsync(context, default);

        // Should NOT have called the build target
        await _dotnetCli.DidNotReceive().RunWithOutputAsync(
            Arg.Is<IReadOnlyList<string>>(args => args.Contains("--getTargetResult:Build")),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());

        string expectedDir = Path.GetDirectoryName(assemblyPath)!;
        Assert.Equal(expectedDir, context.StartupDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public async Task PrepareForHostRunAsync_throws_when_target_result_is_empty()
    {
        string projectFile = Path.Combine(_projectDir.FullName, "MyApp.csproj");
        File.WriteAllText(projectFile, "<Project></Project>");

        _dotnetCli.RunWithOutputAsync(
                Arg.Is<IReadOnlyList<string>>(args => args[0] == "build" && args.Contains("--getTargetResult:Build")),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns("   \n");

        DotNetSourceProject project = CreateProject(projectFile);
        FunctionsProjectHostRunContext context = CreateHostRunContext();

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => project.PrepareForHostRunAsync(context, default));

        Assert.Contains("output directory", ex.Message);
        Assert.True(ex.IsUserError);
    }

    [Fact]
    public async Task PrepareForHostRunAsync_propagates_build_failure()
    {
        string projectFile = Path.Combine(_projectDir.FullName, "MyApp.csproj");
        File.WriteAllText(projectFile, "<Project></Project>");

        _dotnetCli.RunWithOutputAsync(
                Arg.Is<IReadOnlyList<string>>(args => args[0] == "build" && args.Contains("--getTargetResult:Build")),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Throws(new DotnetCliException(1, "Build failed", "", "build MyApp.csproj"));

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
    public void ParseTargetResult_throws_on_invalid_json_with_inner_exception()
    {
        string projectFile = Path.Combine(_projectDir.FullName, "MyApp.csproj");
        File.WriteAllText(projectFile, "<Project></Project>");

        DotNetSourceProject project = CreateProject(projectFile);

        GracefulException ex = Assert.Throws<GracefulException>(
            () => project.ParseTargetResult("not json at all", "Build"));

        Assert.Contains("not valid JSON", ex.Message);
        Assert.IsAssignableFrom<System.Text.Json.JsonException>(ex.InnerException);
        Assert.True(ex.IsUserError);
    }

    [Fact]
    public void ParseTargetResult_throws_when_no_items()
    {
        string projectFile = Path.Combine(_projectDir.FullName, "MyApp.csproj");
        File.WriteAllText(projectFile, "<Project></Project>");

        DotNetSourceProject project = CreateProject(projectFile);
        string json = """
            {
              "TargetResults": {
                "Build": {
                  "Result": "Success",
                  "Items": []
                }
              }
            }
            """;

        GracefulException ex = Assert.Throws<GracefulException>(
            () => project.ParseTargetResult(json, "Build"));

        Assert.Contains("output directory", ex.Message);
        Assert.True(ex.IsUserError);
    }

    private DotNetSourceProject CreateProject(string projectFile)
        => new(WorkingDirectory.FromExplicit(_projectDir.FullName), projectFile, _dotnetCli);

    private FunctionsProjectHostRunContext CreateHostRunContext(bool skipBuild = false)
        => new(_projectDir, "dotnet", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), skipBuild);

    private static string BuildTargetResultJson(string assemblyFullPath, string targetName)
    {
        string escapedFullPath = assemblyFullPath.Replace("\\", "\\\\");
        return $$"""
            {
              "TargetResults": {
                "{{targetName}}": {
                  "Result": "Success",
                  "Items": [
                    {
                      "Identity": "{{escapedFullPath}}",
                      "FullPath": "{{escapedFullPath}}",
                      "Filename": "MyApp",
                      "Extension": ".dll"
                    }
                  ]
                }
              }
            }
            """;
    }
}
