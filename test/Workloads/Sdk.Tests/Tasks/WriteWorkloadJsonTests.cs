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
        doc.RootElement.TryGetProperty("runtimeIdentifier", out _).Should().BeFalse();
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
        WriteWorkloadJson task = CreateTask(outputPath, kind: "rid-pointer");
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
    public void Execute_WorkloadWithEmptyEntryPointAssemblyPath_Fails()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "workload",
            entryPointAssemblyPath: "", entryPointType: "Some.Type");

        task.Execute().Should().BeFalse();
        File.Exists(outputPath).Should().BeFalse();
    }

    [Fact]
    public void Execute_WorkloadWithEmptyEntryPointType_Fails()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "workload",
            entryPointAssemblyPath: "Some.dll", entryPointType: "");

        task.Execute().Should().BeFalse();
        File.Exists(outputPath).Should().BeFalse();
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
    public void Execute_RidImplementation_WritesRuntimeIdentifier()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "content");
        task.RuntimeIdentifier = "win-x64";

        Assert.True(task.Execute());

        JsonDocument doc = ParseOutput(outputPath);
        Assert.Equal("win-x64", doc.RootElement.GetProperty("runtimeIdentifier").GetString());
    }

    [Fact]
    public void Execute_RidPointerWithoutInnerPackages_Fails()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "rid-pointer");

        Assert.False(task.Execute());
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void Execute_NonPointerWithInnerPackages_Fails()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "content");
        task.InnerPackages = [CreateTaskItem("My.Package.win-x64", "win-x64")];

        Assert.False(task.Execute());
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void Execute_PointerWithRuntimeIdentifier_Fails()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "rid-pointer");
        task.RuntimeIdentifier = "win-x64";
        task.InnerPackages = [CreateTaskItem("My.Package.win-x64", "win-x64")];

        Assert.False(task.Execute());
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void Execute_UppercaseRuntimeIdentifier_Fails()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "content");
        task.RuntimeIdentifier = "WIN-X64";

        Assert.False(task.Execute());
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void Execute_PointerWithMismatchedImplementationId_Fails()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "rid-pointer");
        task.InnerPackages = [CreateTaskItem("Other.Package.win-x64", "win-x64")];

        Assert.False(task.Execute());
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void Execute_PointerWithDuplicateRuntimeIdentifiers_Fails()
    {
        string outputPath = Path.Combine(_tempDir, "workload.json");
        WriteWorkloadJson task = CreateTask(outputPath, kind: "rid-pointer");
        task.InnerPackages =
        [
            CreateTaskItem("My.Package.win-x64", "win-x64"),
            CreateTaskItem("My.Package.WIN-X64", "WIN-X64"),
        ];

        Assert.False(task.Execute());
        Assert.False(File.Exists(outputPath));
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
        WriteWorkloadJson task = CreateTask(outputPath, kind: "rid-pointer");
        task.InnerPackages = [CreateTaskItem("Pkg.win-x64", "win-x64")];
        task.PackageId = "Pkg";

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
            PackageId = "My.Package",
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
