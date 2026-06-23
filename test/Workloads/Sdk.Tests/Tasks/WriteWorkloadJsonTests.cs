// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Microsoft.Build.Framework;
using NSubstitute;
using Xunit;

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

        Assert.True(result);
        Assert.True(File.Exists(outputPath));

        JsonDocument doc = ParseOutput(outputPath);
        Assert.Equal("https://example.com/schema.json", doc.RootElement.GetProperty("$schema").GetString());
        Assert.Equal("content", doc.RootElement.GetProperty("kind").GetString());
        Assert.False(doc.RootElement.TryGetProperty("entryPoint", out _));
        Assert.False(doc.RootElement.TryGetProperty("packages", out _));
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

        Assert.True(result);
        JsonDocument doc = ParseOutput(outputPath);
        Assert.Equal("workload", doc.RootElement.GetProperty("kind").GetString());

        JsonElement entryPoint = doc.RootElement.GetProperty("entryPoint");
        Assert.Equal("My.Workload.dll", entryPoint.GetProperty("assemblyPath").GetString());
        Assert.Equal("My.Namespace.MyWorkload", entryPoint.GetProperty("type").GetString());
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

        Assert.True(result);
        JsonDocument doc = ParseOutput(outputPath);
        JsonElement packages = doc.RootElement.GetProperty("packages");
        Assert.Equal("My.Package.win-x64", packages.GetProperty("win-x64").GetString());
        Assert.Equal("My.Package.linux-x64", packages.GetProperty("linux-x64").GetString());
    }

    [Fact]
    public void Execute_NoEntryPointFields_OmitsEntryPoint()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "meta");

        task.Execute();

        JsonDocument doc = ParseOutput(outputPath);
        Assert.False(doc.RootElement.TryGetProperty("entryPoint", out _));
    }

    [Fact]
    public void Execute_EmptyEntryPointAssemblyPath_OmitsEntryPoint()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "workload",
            entryPointAssemblyPath: "", entryPointType: "Some.Type");

        task.Execute();

        JsonDocument doc = ParseOutput(outputPath);
        Assert.False(doc.RootElement.TryGetProperty("entryPoint", out _));
    }

    [Fact]
    public void Execute_EmptyEntryPointType_OmitsEntryPoint()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "workload",
            entryPointAssemblyPath: "Some.dll", entryPointType: "");

        task.Execute();

        JsonDocument doc = ParseOutput(outputPath);
        Assert.False(doc.RootElement.TryGetProperty("entryPoint", out _));
    }

    [Fact]
    public void Execute_EmptyInnerPackages_OmitsPackages()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "content");
        task.InnerPackages = [];

        task.Execute();

        JsonDocument doc = ParseOutput(outputPath);
        Assert.False(doc.RootElement.TryGetProperty("packages", out _));
    }

    [Fact]
    public void Execute_CreatesOutputDirectory()
    {
        string nestedDir = Path.Combine(_tempDir, "nested", "deep");
        string outputPath = Path.Combine(nestedDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "content");

        bool result = task.Execute();

        Assert.True(result);
        Assert.True(Directory.Exists(nestedDir));
        Assert.True(File.Exists(outputPath));
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
        Assert.Equal(firstWriteTime, secondWriteTime);
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
        Assert.NotEqual(contentBefore, contentAfter);
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
