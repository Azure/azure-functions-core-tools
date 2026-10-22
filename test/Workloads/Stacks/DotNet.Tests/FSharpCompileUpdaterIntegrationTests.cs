// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public sealed class FSharpCompileUpdaterIntegrationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-fsharp-compile-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ActualMsBuild_RepairChangesCompileOrder_BuildIncludesNewFunctionBeforeProgram()
    {
        string project = Path.Combine(_root, "Selected.fsproj");
        string program = Path.Combine(_root, "Program.fs");
        string generated = Path.Combine(_root, "Greeting.fs");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup>
              <ItemGroup>
                <Compile Include="Program.fs" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(program, "module Program\n[<EntryPoint>]\nlet main _ = Greeting.answer - 42\n");
        IDotnetPathResolver resolver = Substitute.For<IDotnetPathResolver>();
        resolver.Resolve().Returns("dotnet");
        DotnetCliRunner runner = new(resolver);
        FSharpCompileUpdater updater = new(runner, new());
        string source = "module Greeting\nlet answer = 42\n";
        byte[] original = File.ReadAllBytes(project);

        FSharpCompileUpdatePlan plan = await updater.PlanAsync(project, Hash(original), [program],
            [new(generated, Hash(Encoding.UTF8.GetBytes(source)))], CancellationToken.None);

        File.Exists(generated).Should().BeFalse();
        File.ReadAllBytes(project).Should().Equal(original);
        File.WriteAllText(generated, source, new UTF8Encoding(false));
        await updater.ApplyAsync(plan, CancellationToken.None);
        StringBuilder output = new();
        await runner.RunStreamingAsync(["msbuild", project, "-nologo", "-getItem:Compile"], _root,
            line => output.AppendLine(line), null, CancellationToken.None);
        using var evaluation = JsonDocument.Parse(output.ToString());
        evaluation.RootElement.GetProperty("Items").GetProperty("Compile").EnumerateArray()
            .Select(item => item.GetProperty("Identity").GetString()).Should().Equal("Greeting.fs", "Program.fs");
        await runner.RunAsync(["restore", project, "--source", _root, "-p:NuGetAudit=false"], _root, CancellationToken.None);
        await runner.RunAsync(["build", project, "--no-restore", "-m:1", "-v:quiet"], _root, CancellationToken.None);
        File.Exists(Path.Combine(_root, "bin", "Debug", "net10.0", "Selected.dll")).Should().BeTrue();
        FSharpCompileUpdatePlan repeated = await updater.PlanAsync(project, Hash(File.ReadAllBytes(project)), [generated, program],
            [new(generated, Hash(Encoding.UTF8.GetBytes(source)))], CancellationToken.None);
        repeated.ChangesProject.Should().BeFalse();
        await updater.ApplyAsync(repeated, CancellationToken.None);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AtomicReplacement_ChangedProjectOrCancellation_PreservesUserBytesAndCleansTemporaryFile()
    {
        string project = Path.Combine(_root, "Selected.fsproj");
        File.WriteAllText(project, "user bytes");
        FSharpProjectFiles files = new();
        Func<Task> changed = () => files.ReplaceAsync(project, Hash(Encoding.UTF8.GetBytes("old bytes")),
            Encoding.UTF8.GetBytes("new bytes"), CancellationToken.None);
        await changed.Should().ThrowAsync<InvalidOperationException>().WithMessage("*changed*");
        File.ReadAllText(project).Should().Be("user bytes");
        Directory.GetFiles(_root, ".func-fsharp-*.tmp").Should().BeEmpty();
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        Func<Task> canceled = () => files.ReplaceAsync(project, Hash(File.ReadAllBytes(project)),
            Encoding.UTF8.GetBytes("new bytes"), cancellation.Token);
        await canceled.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllText(project).Should().Be("user bytes");
        Directory.GetFiles(_root, ".func-fsharp-*.tmp").Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void FileGuards_LinkOrOccupiedPath_RefuseWithoutChangingExternalBytes()
    {
        string outside = Path.Combine(_root, "outside.fs");
        File.WriteAllText(outside, "external sentinel");
        string linked = Path.Combine(_root, "Linked.fs");
        File.CreateSymbolicLink(linked, outside);
        FSharpProjectFiles files = new();
        Action link = () => files.ValidatePath(linked, _root, mustExist: true);
        Action occupied = () => files.ValidatePath(_root, Path.GetDirectoryName(_root)!, mustExist: true);

        link.Should().Throw<InvalidOperationException>().WithMessage("*linked*");
        occupied.Should().Throw<InvalidOperationException>().WithMessage("*occupied*");
        File.ReadAllText(outside).Should().Be("external sentinel");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
