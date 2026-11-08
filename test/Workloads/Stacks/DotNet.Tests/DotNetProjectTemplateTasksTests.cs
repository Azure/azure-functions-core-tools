// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public class DotNetProjectTemplateTasksTests : IDisposable
{
    private static readonly Guid _reference = new("B17581D1-C5C9-4489-8F0A-004BE667B814");
    private static readonly Guid _restore = new("210D431B-A78B-4D2F-B762-4ED3E3EA9025");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"func-tasks-{Guid.NewGuid():N}");
    private readonly IDotnetCliRunner _runner = Substitute.For<IDotnetCliRunner>();

    public DotNetProjectTemplateTasksTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "App.csproj"), "<Project />");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("2.1.1", false)]
    [InlineData("2.2.0", false)]
    [InlineData("2.0.0", true)]
    [InlineData("", true)]
    public async Task PackageReference_Preflight_RequiresAnAdequateEffectiveReference(string version, bool fails)
    {
        string json = $$$"""
            {"Items":{"PackageReference":[{"Identity":"Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore","Version":"{{{version}}}"}]}}
            """;
        _runner.When(runner => runner.RunStreamingAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(), Arg.Any<Action<string>?>(), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>()))
            .Do(call => call.ArgAt<Action<string>>(2)(json));
        DotNetProjectStack stack = new(_runner);
        var directory = WorkingDirectory.FromExplicit(_root);
        Dictionary<string, string> arguments = ReferenceArguments();

        if (fails)
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => stack.RequiresExecutionAsync(directory, _reference, arguments, CancellationToken.None));
        }
        else
        {
            Assert.False(await stack.RequiresExecutionAsync(directory, _reference, arguments, CancellationToken.None));
        }

        await _runner.DidNotReceive().RunAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Restore_IsPreflightedAndExecutesOnlyWhenRequested()
    {
        DotNetProjectStack stack = new(_runner);
        var directory = WorkingDirectory.FromExplicit(_root);
        Dictionary<string, string> arguments = [];

        Assert.True(await stack.RequiresExecutionAsync(directory, _restore, arguments, CancellationToken.None));
        await _runner.DidNotReceive().RunAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await stack.ExecuteAsync(directory, _restore, arguments, CancellationToken.None);
        await _runner.Received(1).RunAsync(
            Arg.Is<IReadOnlyList<string>>(args => args.Count == 2 && args[0] == "restore" && args[1] == Path.Combine(_root, "App.csproj")),
            _root, CancellationToken.None);
    }

    [Fact]
    public async Task UnsupportedAction_FailsClosed()
    {
        DotNetProjectStack stack = new(_runner);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            stack.RequiresExecutionAsync(WorkingDirectory.FromExplicit(_root), Guid.NewGuid(), new Dictionary<string, string>(), CancellationToken.None));
    }

    [Fact]
    public async Task EvaluateTemplateCompatibility_PinsEvaluatedInputsAndReturnsBindingsUntilDisposed()
    {
        ConfigureEvaluations(ProjectEvaluationJson("net10.0", "App"));
        DotNetProjectStack stack = new(_runner);
        DotNetSourceProject project = new(WorkingDirectory.FromExplicit(_root), Path.Combine(_root, "App.csproj"), _runner);
        TestLease? lease = null;
        IReadOnlyList<string>? pinnedInputs = null;

        using (ProjectTemplateCompatibility result = await stack.EvaluateTemplateCompatibilityAsync(
            project,
            (inputs, _) =>
            {
                pinnedInputs = inputs;
                lease = new TestLease();
                return lease;
            },
            CancellationToken.None))
        {
            Assert.True(result.IsCompatible);
            Assert.Null(result.Diagnostic);
            Assert.Equal("net10.0", result.Bindings["msbuild:TargetFramework"]);
            Assert.Equal("App", result.Bindings["namespace"]);
            Assert.Contains(Path.Combine(_root, "App.csproj"), pinnedInputs!, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(Path.Combine(_root, "Directory.Build.props"), pinnedInputs!, StringComparer.OrdinalIgnoreCase);
            Assert.False(lease!.IsDisposed);
        }

        Assert.True(lease!.IsDisposed);
        await _runner.Received(2).RunStreamingAsync(
            Arg.Is<IReadOnlyList<string>>(args => args.Contains("-getItem:PackageReference")),
            _root,
            Arg.Any<Action<string>?>(),
            Arg.Any<Action<string>?>(),
            CancellationToken.None);
    }

    [Fact]
    public async Task EvaluateTemplateCompatibility_ChangedEvaluationDisposesPinnedInputsAndFailsClosed()
    {
        ConfigureEvaluations(ProjectEvaluationJson("net10.0", "App"), ProjectEvaluationJson("net10.0", "Changed"));
        DotNetProjectStack stack = new(_runner);
        DotNetSourceProject project = new(WorkingDirectory.FromExplicit(_root), Path.Combine(_root, "App.csproj"), _runner);
        TestLease? lease = null;

        await Assert.ThrowsAsync<InvalidDataException>(() => stack.EvaluateTemplateCompatibilityAsync(
            project,
            (_, _) => lease = new TestLease(),
            CancellationToken.None));

        Assert.True(lease!.IsDisposed);
    }

    [Fact]
    public async Task EvaluateTemplateCompatibility_RejectsReportedInputsAboveTheLimitWithExactCount()
    {
        string reportedInputs = string.Join(';', Enumerable.Range(0, 256)
            .Select(index => Path.Combine(_root, $"Import{index}.props")));
        ConfigureEvaluations(ProjectEvaluationJson("net10.0", "App", reportedInputs));
        DotNetProjectStack stack = new(_runner);
        DotNetSourceProject project = new(WorkingDirectory.FromExplicit(_root), Path.Combine(_root, "App.csproj"), _runner);
        bool pinCalled = false;

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            stack.EvaluateTemplateCompatibilityAsync(
                project,
                (_, _) =>
                {
                    pinCalled = true;
                    return new TestLease();
                },
                CancellationToken.None));

        Assert.Contains("257 evaluated project inputs", exception.Message, StringComparison.Ordinal);
        Assert.Contains("256-input safety limit", exception.Message, StringComparison.Ordinal);
        Assert.False(pinCalled);
    }

    [Fact]
    public async Task EvaluateTemplateCompatibility_NormalizesDotnetEvaluationFailure()
    {
        _runner.RunStreamingAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<string>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new DotnetCliException(1, "project evaluation failed", "", "msbuild")));
        DotNetProjectStack stack = new(_runner);
        DotNetSourceProject project = new(WorkingDirectory.FromExplicit(_root), Path.Combine(_root, "App.csproj"), _runner);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            stack.EvaluateTemplateCompatibilityAsync(project, (_, _) => new TestLease(), CancellationToken.None));

        Assert.Contains("project evaluation failed", exception.Message, StringComparison.Ordinal);
        Assert.IsType<DotnetCliException>(exception.InnerException);
    }

    [Fact]
    public async Task EvaluateTemplateCompatibility_IncompatibleFrameworkRetainsEvidenceUntilCallerDisposes()
    {
        ConfigureEvaluations(ProjectEvaluationJson("net8.0", "App"));
        DotNetProjectStack stack = new(_runner);
        DotNetSourceProject project = new(WorkingDirectory.FromExplicit(_root), Path.Combine(_root, "App.csproj"), _runner);
        TestLease? lease = null;

        using ProjectTemplateCompatibility result = await stack.EvaluateTemplateCompatibilityAsync(
            project,
            (_, _) => lease = new TestLease(),
            CancellationToken.None);

        Assert.False(result.IsCompatible);
        Assert.Contains("net10.0", result.Diagnostic, StringComparison.Ordinal);
        Assert.False(lease!.IsDisposed);
    }

    private static Dictionary<string, string> ReferenceArguments() => new()
    {
        ["referenceType"] = "package",
        ["reference"] = "Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore",
        ["version"] = "2.1.1",
        ["projectFileExtensions"] = ".csproj",
    };

    private string ProjectEvaluationJson(string targetFramework, string rootNamespace, string? projectInputs = null) =>
        JsonSerializer.Serialize(new
        {
            Properties = new Dictionary<string, string>
            {
                ["MSBuildAllProjects"] = projectInputs ?? Path.Combine(_root, "Directory.Build.props"),
                ["TargetFramework"] = targetFramework,
                ["RootNamespace"] = rootNamespace,
                ["OutputType"] = "Exe",
                ["AzureFunctionsVersion"] = "v4",
            },
            Items = new
            {
                PackageReference = new[]
                {
                    new { Identity = "Microsoft.Azure.Functions.Worker", Version = "2.50.0" },
                    new { Identity = "Microsoft.Azure.Functions.Worker.Sdk", Version = "2.0.5" },
                    new { Identity = "Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore", Version = "2.1.1" },
                },
            },
        });

    private void ConfigureEvaluations(params string[] evaluations)
    {
        int index = 0;
        _runner.When(runner => runner.RunStreamingAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(), Arg.Any<Action<string>?>(), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>()))
            .Do(call => call.ArgAt<Action<string>>(2)(evaluations[Math.Min(index++, evaluations.Length - 1)]));
    }

    private sealed class TestLease : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
