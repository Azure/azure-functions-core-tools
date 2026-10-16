// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public sealed class FSharpCompileUpdaterTests
{
    private readonly IDotnetCliRunner _runner = Substitute.For<IDotnetCliRunner>();
    private readonly MemoryFiles _files = new();
    private readonly string _directory = Path.GetFullPath("selected-fsharp");

    private string Project => Path.Combine(_directory, "Selected.fsproj");
    private string Program => Path.Combine(_directory, "Program.fs");
    private string Generated => Path.Combine(_directory, "Http.fs");

    private const string Original = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project Sdk="Microsoft.NET.Sdk">
          <!-- keep exact unrelated content and single quotes -->
          <Import Project='owned.props' />
          <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
          <ItemGroup>
            <Compile Include="Program.fs" />
          </ItemGroup>
        </Project>
        """;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task PlanAndApply_QualifiedSnapshot_PreservesEveryUnrelatedByteAndInsertsBeforeProgram(bool bom, bool crlf)
    {
        string normalized = Original.Replace("\r\n", "\n", StringComparison.Ordinal);
        string original = crlf ? normalized.Replace("\n", "\r\n", StringComparison.Ordinal) : normalized;
        byte[] body = Encoding.UTF8.GetBytes(original);
        byte[] bytes = bom ? [0xef, 0xbb, 0xbf, .. body] : body;
        Setup(bytes);
        FSharpCompileUpdater updater = new(_runner, _files);
        string expected = original.Replace("    <Compile Include=\"Program.fs\" />",
            "    <Compile Include=\"Http.fs\" />" + (crlf ? "\r\n" : "\n") + "    <Compile Include=\"Program.fs\" />", StringComparison.Ordinal);

        FSharpCompileUpdatePlan plan = await updater.PlanAsync(Project, Hash(bytes), [Program],
            [new(Generated, Hash("native generated"))], CancellationToken.None);

        plan.ChangesProject.Should().BeTrue();
        _files.Writes.Should().Be(0);
        _files.Content[Generated] = Encoding.UTF8.GetBytes("native generated");
        await updater.ApplyAsync(plan, CancellationToken.None);
        _files.Content[Project].Should().Equal(bom ? [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(expected)] : Encoding.UTF8.GetBytes(expected));
        _files.Writes.Should().Be(1);
    }

    [Fact]
    public async Task PlanAndApply_AlreadyIncludedInExactOrder_IsIdempotent()
    {
        string original = Original.Replace("    <Compile", "    <Compile Include=\"Http.fs\" />\n    <Compile", StringComparison.Ordinal);
        Setup(Encoding.UTF8.GetBytes(original), [Generated, Program]);
        _files.Content[Generated] = Encoding.UTF8.GetBytes("native generated");
        FSharpCompileUpdater updater = new(_runner, _files);

        FSharpCompileUpdatePlan plan = await updater.PlanAsync(Project, Hash(original), [Generated, Program],
            [new(Generated, Hash("native generated"))], CancellationToken.None);
        await updater.ApplyAsync(plan, CancellationToken.None);

        plan.ChangesProject.Should().BeFalse();
        _files.Writes.Should().Be(0);
        Encoding.UTF8.GetString(_files.Content[Project]).Should().Be(original);
    }

    [Theory]
    [InlineData("<Compile Include=\"*.fs\" />")]
    [InlineData("<Compile Include=\"Program.fs\" Condition=\"true\" />")]
    [InlineData("<Compile Update=\"Program.fs\" />")]
    [InlineData("<Compile Remove=\"Program.fs\" />")]
    [InlineData("<Compile Include=\"Program.fs\"><Link>Program.fs</Link></Compile>")]
    [InlineData("<Compile Include=\"Program.fs;Other.fs\" />")]
    [InlineData("<Compile Include=\"../Program.fs\" />")]
    [InlineData("<Compile Include=\"$(Source).fs\" />")]
    [InlineData("<Compile Include=\"Program.fs\" /><Compile Include=\"Program.fs\" />")]
    public async Task Plan_UnsupportedCompileXml_RefusesBeforeMutation(string element)
    {
        string original = Original.Replace("<Compile Include=\"Program.fs\" />", element, StringComparison.Ordinal);
        Setup(Encoding.UTF8.GetBytes(original));
        FSharpCompileUpdater updater = new(_runner, _files);
        Func<Task> action = () => updater.PlanAsync(Project, Hash(original), [Program], [new(Generated, Hash("native generated"))], CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
        _files.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("wrongOrder")]
    [InlineData("imported")]
    [InlineData("net8")]
    [InlineData("missing")]
    public async Task Plan_EvaluatedCompileMismatch_Refuses(string mismatch)
    {
        Setup(Encoding.UTF8.GetBytes(Original), evaluatedOrder: mismatch == "wrongOrder" ? [Generated, Program] : [Program],
            definingProject: mismatch == "imported" ? Path.Combine(_directory, "import.props") : Project,
            framework: mismatch == "net8" ? "net8.0" : "net10.0", missing: mismatch == "missing");
        FSharpCompileUpdater updater = new(_runner, _files);
        Func<Task> action = () => updater.PlanAsync(Project, Hash(Original), [Program], [new(Generated, Hash("native generated"))], CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
        _files.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("source")]
    [InlineData("link")]
    [InlineData("commit")]
    public async Task Apply_ChangedInputOrPath_RefusesAndPreservesProject(string change)
    {
        Setup(Encoding.UTF8.GetBytes(Original));
        FSharpCompileUpdater updater = new(_runner, _files);
        FSharpCompileUpdatePlan plan = await updater.PlanAsync(Project, Hash(Original), [Program],
            [new(Generated, Hash("native generated"))], CancellationToken.None);
        _files.Content[Generated] = Encoding.UTF8.GetBytes(change == "source" ? "different" : "native generated");
        if (change == "project")
        {
            _files.Content[Project] = Encoding.UTF8.GetBytes(Original + "\n<!-- changed -->");
        }
        _files.DenyPath = change == "link" ? Generated : null;
        _files.BeforeReplace = change == "commit" ? () => _files.Content[Project] = Encoding.UTF8.GetBytes("user change") : null;
        byte[] before = [.. _files.Content[Project]];
        Func<Task> action = () => updater.ApplyAsync(plan, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
        _files.Writes.Should().Be(0);
        _files.Content[Project].Should().Equal(change == "commit" ? Encoding.UTF8.GetBytes("user change") : before);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("program")]
    [InlineData("outside")]
    [InlineData("relative")]
    [InlineData("digest")]
    public async Task Plan_UnsafeGeneratedContract_Refuses(string invalid)
    {
        Setup(Encoding.UTF8.GetBytes(Original));
        FSharpGeneratedFile file = new(invalid switch
        {
            "program" => Program,
            "outside" => Path.GetFullPath("outside.fs"),
            "relative" => "Http.fs",
            _ => Generated,
        }, invalid == "digest" ? "invalid" : Hash("native generated"));
        FSharpCompileUpdater updater = new(_runner, _files);
        Func<Task> action = () => updater.PlanAsync(Project, Hash(Original), [Program],
            invalid == "duplicate" ? [file, file] : [file], CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
        _files.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("afterProgram")]
    [InlineData("conditionalGroup")]
    [InlineData("notStandalone")]
    public async Task Plan_AmbiguousOrUnqualifiedOrder_Refuses(string invalid)
    {
        string original = invalid switch
        {
            "partial" => Original.Replace("<Compile Include=\"Program.fs\" />", "<Compile Include=\"Http.fs\" />\n    <Compile Include=\"Program.fs\" />", StringComparison.Ordinal),
            "afterProgram" => Original.Replace("<Compile Include=\"Program.fs\" />", "<Compile Include=\"Program.fs\" />\n    <Compile Include=\"Http.fs\" />", StringComparison.Ordinal),
            "conditionalGroup" => Original.Replace("<ItemGroup>", "<ItemGroup Condition=\"true\">", StringComparison.Ordinal),
            _ => Original.Replace("  <ItemGroup>\r\n", "  <ItemGroup>", StringComparison.Ordinal)
                .Replace("  <ItemGroup>\n", "  <ItemGroup>", StringComparison.Ordinal),
        };
        string[] order = invalid == "partial" ? [Generated, Program] : invalid == "afterProgram" ? [Program, Generated] : [Program];
        Setup(Encoding.UTF8.GetBytes(original), order);
        FSharpCompileUpdater updater = new(_runner, _files);
        IReadOnlyList<FSharpGeneratedFile> generated = invalid == "partial"
            ? [new(Generated, Hash("native")), new(Path.Combine(_directory, "Other.fs"), Hash("other"))]
            : [new(Generated, Hash("native"))];
        Func<Task> action = () => updater.PlanAsync(Project, Hash(original), order, generated, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
        _files.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Plan_ProjectChangedDuringEvaluation_RefusesWithoutNativeWrites()
    {
        Setup(Encoding.UTF8.GetBytes(Original));
        _runner.When(runner => runner.RunStreamingAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(),
            Arg.Any<Action<string>>(), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>()))
            .Do(_ => _files.Content[Project] = Encoding.UTF8.GetBytes("changed during evaluation"));
        FSharpCompileUpdater updater = new(_runner, _files);
        Func<Task> action = () => updater.PlanAsync(Project, Hash(Original), [Program], [new(Generated, Hash("native"))], CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*changed during*");
        _files.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Apply_IdempotentProjectChangedDuringEvaluation_RefusesInsteadOfReturningNoOpSuccess()
    {
        string original = Original.Replace("    <Compile", "    <Compile Include=\"Http.fs\" />\n    <Compile", StringComparison.Ordinal);
        Setup(Encoding.UTF8.GetBytes(original), [Generated, Program]);
        _files.Content[Generated] = Encoding.UTF8.GetBytes("native generated");
        FSharpCompileUpdater updater = new(_runner, _files);
        FSharpCompileUpdatePlan plan = await updater.PlanAsync(Project, Hash(original), [Generated, Program],
            [new(Generated, Hash("native generated"))], CancellationToken.None);
        _runner.When(runner => runner.RunStreamingAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(),
            Arg.Any<Action<string>>(), Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>()))
            .Do(_ => _files.Content[Project] = Encoding.UTF8.GetBytes("changed during no-op evaluation"));
        Func<Task> action = () => updater.ApplyAsync(plan, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*changed during*");
        _files.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Cancellation_PreflightAndApply_DoesNotWrite()
    {
        Setup(Encoding.UTF8.GetBytes(Original));
        FSharpCompileUpdater updater = new(_runner, _files);
        FSharpCompileUpdatePlan plan = await updater.PlanAsync(Project, Hash(Original), [Program],
            [new(Generated, Hash("native generated"))], CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        Func<Task> preview = () => updater.PlanAsync(Project, Hash(Original), [Program],
            [new(Generated, Hash("native generated"))], cancellation.Token);
        Func<Task> apply = () => updater.ApplyAsync(plan, cancellation.Token);

        await preview.Should().ThrowAsync<OperationCanceledException>();
        await apply.Should().ThrowAsync<OperationCanceledException>();
        _files.Writes.Should().Be(0);
    }

    [Fact]
    public void Constructor_NullDependencies_Throws()
    {
        Action runner = () => new FSharpCompileUpdater(null!, _files);
        Action files = () => new FSharpCompileUpdater(_runner, null!);
        runner.Should().Throw<ArgumentNullException>().WithParameterName("dotnetCli");
        files.Should().Throw<ArgumentNullException>().WithParameterName("files");
    }

    private void Setup(byte[] project, string[]? evaluatedOrder = null, string? definingProject = null,
        string framework = "net10.0", bool missing = false)
    {
        _files.Content[Project] = project;
        _files.Content[Program] = Encoding.UTF8.GetBytes("program");
        string json = missing ? "{}" : JsonSerializer.Serialize(new
        {
            Properties = new { TargetFramework = framework },
            Items = new { Compile = (evaluatedOrder ?? [Program]).Select(path => new { FullPath = path, DefiningProjectFullPath = definingProject ?? Project }) },
        });
        _runner.RunStreamingAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(), Arg.Any<Action<string>>(),
            Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                call.ArgAt<Action<string>>(2)(json);
                return Task.CompletedTask;
            });
    }

    private static string Hash(string text) => Hash(Encoding.UTF8.GetBytes(text));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed class MemoryFiles : FSharpProjectFiles
    {
        public Dictionary<string, byte[]> Content { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? DenyPath { get; set; }
        public int Writes { get; private set; }
        public Action? BeforeReplace { get; set; }

        public override Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Content.TryGetValue(path, out byte[]? bytes) ? bytes : throw new FileNotFoundException(path));
        }

        public override void ValidatePath(string path, string projectDirectory, bool mustExist)
        {
            if (path == DenyPath || (mustExist && !Content.ContainsKey(path)))
            {
                throw new InvalidOperationException("Missing or linked path.");
            }
        }

        public override Task ReplaceAsync(string projectPath, string expectedSha256, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeforeReplace?.Invoke();
            if (Hash(Content[projectPath]) != expectedSha256)
            {
                throw new InvalidOperationException("Changed before commit.");
            }
            Content[projectPath] = content.ToArray();
            Writes++;
            return Task.CompletedTask;
        }
    }
}
