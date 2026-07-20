// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using NSubstitute;

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

        SetupStreamingBuild("--getTargetResult:Build", json);

        DotNetSourceProject project = CreateProject(projectFile);
        FunctionsProjectHostRunContext context = CreateHostRunContext();

        await project.PrepareForHostRunAsync(context, default);

        string expectedDir = Path.GetDirectoryName(assemblyPath)!;
        context.StartupDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar).Should().Be(expectedDir);
    }

    [Fact]
    public async Task PrepareForHostRunAsync_streams_build_output_to_reporter()
    {
        string projectFile = Path.Combine(_projectDir.FullName, "MyApp.csproj");
        File.WriteAllText(projectFile, "<Project></Project>");

        string assemblyPath = Path.Combine(_projectDir.FullName, "bin", "Debug", "net10.0", "MyApp.dll");
        string json = BuildTargetResultJson(assemblyPath, "Build");

        SetupStreamingBuild(
            "--getTargetResult:Build",
            json,
            outputLines: ["  MyApp -> MyApp.dll", "Build succeeded."],
            errorLines: ["warning XYZ: heads up"]);

        var reporter = new CapturingReporter();
        DotNetSourceProject project = CreateProject(projectFile);
        FunctionsProjectHostRunContext context = CreateHostRunContext();
        context.Reporter = reporter;

        await project.PrepareForHostRunAsync(context, default);

        reporter.Logs.Should().Contain(entry => entry.Severity == FunctionsProjectReportSeverity.Info && entry.Line == "Build succeeded.");
        reporter.Logs.Should().Contain(entry => entry.Severity == FunctionsProjectReportSeverity.Error && entry.Line == "warning XYZ: heads up");
    }

    [Fact]
    public async Task PrepareForHostRunAsync_skip_build_uses_get_target_path()
    {
        string projectFile = Path.Combine(_projectDir.FullName, "MyApp.csproj");
        File.WriteAllText(projectFile, "<Project></Project>");

        string assemblyPath = Path.Combine(_projectDir.FullName, "bin", "Debug", "net10.0", "MyApp.dll");
        string json = BuildTargetResultJson(assemblyPath, "GetTargetPath");

        // --no-build trusts existing output, so the assembly must actually be on disk.
        Directory.CreateDirectory(Path.GetDirectoryName(assemblyPath)!);
        File.WriteAllText(assemblyPath, string.Empty);

        SetupStreamingBuild("--getTargetResult:GetTargetPath", json);

        DotNetSourceProject project = CreateProject(projectFile);
        FunctionsProjectHostRunContext context = CreateHostRunContext(skipBuild: true);

        await project.PrepareForHostRunAsync(context, default);

        // Should NOT have called the build target
        await _dotnetCli.DidNotReceive().RunStreamingAsync(
            Arg.Is<IReadOnlyList<string>>(args => args.Contains("--getTargetResult:Build")),
            Arg.Any<string?>(),
            Arg.Any<Action<string>?>(),
            Arg.Any<Action<string>?>(),
            Arg.Any<CancellationToken>());

        string expectedDir = Path.GetDirectoryName(assemblyPath)!;
        context.StartupDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar).Should().Be(expectedDir);
    }

    [Fact]
    public async Task PrepareForHostRunAsync_skip_build_throws_when_assembly_missing()
    {
        string projectFile = Path.Combine(_projectDir.FullName, "MyApp.csproj");
        File.WriteAllText(projectFile, "<Project></Project>");

        // Resolve a valid target path, but never create the assembly on disk.
        string assemblyPath = Path.Combine(_projectDir.FullName, "bin", "Debug", "net10.0", "MyApp.dll");
        string json = BuildTargetResultJson(assemblyPath, "GetTargetPath");

        SetupStreamingBuild("--getTargetResult:GetTargetPath", json);

        DotNetSourceProject project = CreateProject(projectFile);
        FunctionsProjectHostRunContext context = CreateHostRunContext(skipBuild: true);

        GracefulException ex = (await FluentActions.Awaiting(() => project.PrepareForHostRunAsync(context, default)).Should().ThrowAsync<GracefulException>()).Which;

        ex.Message.Should().Contain("--no-build");
        ex.Message.Should().Contain("MyApp");
        ex.IsUserError.Should().BeTrue();
    }

    [Fact]
    public async Task PrepareForHostRunAsync_skip_build_propagates_cli_failure()
    {
        string projectFile = Path.Combine(_projectDir.FullName, "MyApp.csproj");
        File.WriteAllText(projectFile, "<Project></Project>");

        SetupStreamingFailure(
            "--getTargetResult:GetTargetPath",
            new DotnetCliException(1, "Target path resolution failed", "", "build MyApp.csproj"));

        DotNetSourceProject project = CreateProject(projectFile);
        FunctionsProjectHostRunContext context = CreateHostRunContext(skipBuild: true);

        GracefulException ex = (await FluentActions.Awaiting(() => project.PrepareForHostRunAsync(context, default)).Should().ThrowAsync<GracefulException>()).Which;

        ex.Message.Should().Contain("dotnet build");
        ex.Message.Should().Contain("exit 1");
        ex.IsUserError.Should().BeTrue();
    }

    [Fact]
    public async Task PrepareForHostRunAsync_throws_when_target_result_is_empty()
    {
        string projectFile = Path.Combine(_projectDir.FullName, "MyApp.csproj");
        File.WriteAllText(projectFile, "<Project></Project>");

        SetupStreamingBuild("--getTargetResult:Build", "   \n");

        DotNetSourceProject project = CreateProject(projectFile);
        FunctionsProjectHostRunContext context = CreateHostRunContext();

        GracefulException ex = (await FluentActions.Awaiting(() => project.PrepareForHostRunAsync(context, default)).Should().ThrowAsync<GracefulException>()).Which;

        ex.Message.Should().Contain("output directory");
        ex.IsUserError.Should().BeTrue();
    }

    [Fact]
    public async Task PrepareForHostRunAsync_propagates_build_failure()
    {
        string projectFile = Path.Combine(_projectDir.FullName, "MyApp.csproj");
        File.WriteAllText(projectFile, "<Project></Project>");

        SetupStreamingFailure(
            "--getTargetResult:Build",
            new DotnetCliException(1, "Build failed", "", "build MyApp.csproj"));

        DotNetSourceProject project = CreateProject(projectFile);
        FunctionsProjectHostRunContext context = CreateHostRunContext();

        GracefulException ex = (await FluentActions.Awaiting(() => project.PrepareForHostRunAsync(context, default)).Should().ThrowAsync<GracefulException>()).Which;

        ex.Message.Should().Contain("dotnet build");
        ex.Message.Should().Contain("exit 1");
        ex.IsUserError.Should().BeTrue();
    }

    [Fact]
    public async Task PrepareForHostRunAsync_throws_on_null_context()
    {
        string projectFile = Path.Combine(_projectDir.FullName, "MyApp.csproj");
        File.WriteAllText(projectFile, "<Project></Project>");

        DotNetSourceProject project = CreateProject(projectFile);

        await FluentActions.Awaiting(() => project.PrepareForHostRunAsync(null!, default)).Should().ThrowAsync<ArgumentNullException>();
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

        await FluentActions.Awaiting(() => project.PrepareForHostRunAsync(context, cts.Token)).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void ParseTargetResult_throws_on_invalid_json_with_inner_exception()
    {
        string projectFile = Path.Combine(_projectDir.FullName, "MyApp.csproj");
        File.WriteAllText(projectFile, "<Project></Project>");

        DotNetSourceProject project = CreateProject(projectFile);

        GracefulException ex = FluentActions.Invoking(() => project.ParseTargetResult("not json at all", "Build")).Should().ThrowExactly<GracefulException>().Which;

        ex.Message.Should().Contain("not valid JSON");
        ex.InnerException.Should().BeAssignableTo<System.Text.Json.JsonException>();
        ex.IsUserError.Should().BeTrue();
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

        GracefulException ex = FluentActions.Invoking(() => project.ParseTargetResult(json, "Build")).Should().ThrowExactly<GracefulException>().Which;

        ex.Message.Should().Contain("output directory");
        ex.IsUserError.Should().BeTrue();
    }

    [Theory]
    [InlineData("MyApp.csproj", "C#")]
    [InlineData("MyApp.fsproj", "F#")]
    [InlineData("MyApp.FsProj", "F#")]
    public void Language_FromProjectFileExtension(string projectFileName, string expectedLanguage)
    {
        string projectFile = Path.Combine(_projectDir.FullName, projectFileName);
        DotNetSourceProject project = CreateProject(projectFile);

        project.Language.Should().Be(expectedLanguage);
    }

    private DotNetSourceProject CreateProject(string projectFile)
        => new(WorkingDirectory.FromExplicit(_projectDir.FullName), projectFile, _dotnetCli);

    private FunctionsProjectHostRunContext CreateHostRunContext(bool skipBuild = false)
        => new(_projectDir, "dotnet", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), skipBuild);

    private void SetupStreamingBuild(string targetResultArg, string json, string[]? outputLines = null, string[]? errorLines = null)
    {
        _dotnetCli
            .When(x => x.RunStreamingAsync(
                Arg.Is<IReadOnlyList<string>>(args => args.Contains(targetResultArg)),
                Arg.Any<string?>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<CancellationToken>()))
            .Do(callInfo =>
            {
                IReadOnlyList<string> args = callInfo.ArgAt<IReadOnlyList<string>>(0);
                Action<string>? onOutput = callInfo.ArgAt<Action<string>?>(2);
                Action<string>? onError = callInfo.ArgAt<Action<string>?>(3);

                foreach (string line in outputLines ?? [])
                {
                    onOutput?.Invoke(line);
                }

                foreach (string line in errorLines ?? [])
                {
                    onError?.Invoke(line);
                }

                // Mirror MSBuild's --getResultOutputFile behavior: the structured result is written to the
                // file path supplied in the arguments rather than to stdout.
                File.WriteAllText(GetResultFilePath(args), json);
            });
    }

    private void SetupStreamingFailure(string targetResultArg, Exception exception)
    {
        _dotnetCli
            .When(x => x.RunStreamingAsync(
                Arg.Is<IReadOnlyList<string>>(args => args.Contains(targetResultArg)),
                Arg.Any<string?>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => throw exception);
    }

    private static string GetResultFilePath(IReadOnlyList<string> args)
    {
        const string prefix = "--getResultOutputFile:";
        string arg = args.First(a => a.StartsWith(prefix, StringComparison.Ordinal));
        return arg[prefix.Length..];
    }

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
