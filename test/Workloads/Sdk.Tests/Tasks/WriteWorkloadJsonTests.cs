// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Microsoft.Build.Framework;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.Sdk.Tasks.Tests;

public sealed class WriteWorkloadJsonTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public WriteWorkloadJsonTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Execute_ContentKind_WritesMinimalJson()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "content");

        bool result = task.Execute();

        result.Should().BeTrue();
        File.Exists(outputPath).Should().BeTrue();

        JsonDocument doc = ParseOutput(outputPath);
        doc.RootElement.GetProperty("$schema").GetString().Should().Be("https://example.com/schema.json");
        doc.RootElement.GetProperty("kind").GetString().Should().Be("content");
        doc.RootElement.TryGetProperty("entryPoint", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("packages", out _).Should().BeFalse();
    }

    [Fact]
    public void Execute_WorkloadKind_WritesEntryPoint()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task = CreateTask(
            outputPath,
            kind: "workload",
            entryPointAssemblyPath: "My.Workload.dll",
            entryPointType: "My.Namespace.MyWorkload");

        bool result = task.Execute();

        result.Should().BeTrue();
        JsonDocument doc = ParseOutput(outputPath);
        doc.RootElement.GetProperty("kind").GetString().Should().Be("workload");

        JsonElement entryPoint = doc.RootElement.GetProperty("entryPoint");
        entryPoint.GetProperty("assemblyPath").GetString().Should().Be("My.Workload.dll");
        entryPoint.GetProperty("type").GetString().Should().Be("My.Namespace.MyWorkload");
    }

    [Fact]
    public void Execute_WithInnerPackages_WritesPackagesMap()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "workload",
            entryPointAssemblyPath: "W.dll", entryPointType: "W.Type");
        task.InnerPackages =
        [
            CreateTaskItem("My.Package.win-x64", "win-x64"),
            CreateTaskItem("My.Package.linux-x64", "linux-x64"),
        ];

        bool result = task.Execute();

        result.Should().BeTrue();
        JsonDocument doc = ParseOutput(outputPath);
        JsonElement packages = doc.RootElement.GetProperty("packages");
        packages.GetProperty("win-x64").GetString().Should().Be("My.Package.win-x64");
        packages.GetProperty("linux-x64").GetString().Should().Be("My.Package.linux-x64");
    }

    [Fact]
    public void Execute_NoEntryPointFields_OmitsEntryPoint()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "meta");

        task.Execute();

        JsonDocument doc = ParseOutput(outputPath);
        doc.RootElement.TryGetProperty("entryPoint", out _).Should().BeFalse();
    }

    [Fact]
    public void Execute_EmptyEntryPointAssemblyPath_OmitsEntryPoint()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "workload",
            entryPointAssemblyPath: "", entryPointType: "Some.Type");

        task.Execute();

        JsonDocument doc = ParseOutput(outputPath);
        doc.RootElement.TryGetProperty("entryPoint", out _).Should().BeFalse();
    }

    [Fact]
    public void Execute_EmptyEntryPointType_OmitsEntryPoint()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "workload",
            entryPointAssemblyPath: "Some.dll", entryPointType: "");

        task.Execute();

        JsonDocument doc = ParseOutput(outputPath);
        doc.RootElement.TryGetProperty("entryPoint", out _).Should().BeFalse();
    }

    [Fact]
    public void Execute_EmptyInnerPackages_OmitsPackages()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "content");
        task.InnerPackages = [];

        task.Execute();

        JsonDocument doc = ParseOutput(outputPath);
        doc.RootElement.TryGetProperty("packages", out _).Should().BeFalse();
    }

    [Fact]
    public void Execute_CreatesOutputDirectory()
    {
        string nestedDir = Path.Combine(_tempDir, "nested", "deep");
        string outputPath = Path.Combine(nestedDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "content");

        bool result = task.Execute();

        result.Should().BeTrue();
        Directory.Exists(nestedDir).Should().BeTrue();
        File.Exists(outputPath).Should().BeTrue();
    }

    [Fact]
    public void Execute_SameContent_DoesNotRewrite()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "content");
        task.Execute();

        DateTime firstWriteTime = File.GetLastWriteTimeUtc(outputPath);

        // Small delay to ensure timestamp would differ if rewritten.
        Thread.Sleep(50);

        WriteWorkloadJson task2 = CreateTask(outputPath, kind: "content");
        task2.Execute();

        DateTime secondWriteTime = File.GetLastWriteTimeUtc(outputPath);
        secondWriteTime.Should().Be(firstWriteTime);
    }

    [Fact]
    public void Execute_DifferentContent_Rewrites()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task1 = CreateTask(outputPath, kind: "content");
        task1.Execute();

        string contentBefore = File.ReadAllText(outputPath);

        WriteWorkloadJson task2 = CreateTask(outputPath, kind: "workload",
            entryPointAssemblyPath: "A.dll", entryPointType: "A.Type");
        task2.Execute();

        string contentAfter = File.ReadAllText(outputPath);
        contentAfter.Should().NotBe(contentBefore);
    }

    [Fact]
    public void Execute_OutputIsValidJson()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "workload",
            entryPointAssemblyPath: "Test.dll", entryPointType: "Test.Workload");
        task.InnerPackages = [CreateTaskItem("Pkg.win-x64", "win-x64")];

        task.Execute();

        // Should not throw — validates well-formed JSON.
        string json = File.ReadAllText(outputPath);
        JsonDocument.Parse(json);
    }

    private static WriteWorkloadJson CreateTask(
        string outputPath,
        string kind,
        string schema = "https://example.com/schema.json",
        string entryPointAssemblyPath = "",
        string entryPointType = "")
    {
        return new WriteWorkloadJson
        {
            BuildEngine = Substitute.For<IBuildEngine>(),
            OutputPath = outputPath,
            Schema = schema,
            Kind = kind,
            EntryPointAssemblyPath = entryPointAssemblyPath,
            EntryPointType = entryPointType,
            InnerPackages = [],
        };
    }

    private static ITaskItem CreateTaskItem(string itemSpec, string runtimeIdentifier)
    {
        ITaskItem item = Substitute.For<ITaskItem>();
        item.ItemSpec.Returns(itemSpec);
        item.GetMetadata("RuntimeIdentifier").Returns(runtimeIdentifier);
        return item;
    }

    private static JsonDocument ParseOutput(string path)
    {
        string json = File.ReadAllText(path);
        return JsonDocument.Parse(json);
    }
}
