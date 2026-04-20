// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Workloads;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

[Collection(nameof(WorkingDirectoryTests))]
public class InitCommandTests : IDisposable
{
    private static readonly string _safeDir = Path.GetTempPath();
    private readonly string _tempDir;
    private readonly TestInteractionService _interaction;
    private readonly WorkloadManager _workloadManager;

    public InitCommandTests()
    {
        Directory.SetCurrentDirectory(_safeDir);
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _interaction = new TestInteractionService();

        var workloadDir = Path.Combine(_tempDir, "workloads");
        Directory.CreateDirectory(workloadDir);
        _workloadManager = new WorkloadManager(_interaction, workloadDir);
    }

    public void Dispose()
    {
        try { Directory.SetCurrentDirectory(_safeDir); } catch { }
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void InitCommand_HasExpectedOptions()
    {
        var cmd = new InitCommand(_interaction);
        var optionNames = cmd.Options.Select(o => o.Name).ToList();

        Assert.Contains("--worker-runtime", optionNames);
        Assert.Contains("--name", optionNames);
        Assert.Contains("--language", optionNames);
        Assert.Contains("--force", optionNames);
    }

    [Fact]
    public void InitCommand_RegisteredInParser()
    {
        var root = Parser.CreateCommand(_interaction);
        var names = root.Subcommands.Select(c => c.Name).ToList();

        Assert.Contains("init", names);
    }

    [Fact]
    public async Task RunInitAsync_NoWorkloads_ShowsInstallHint()
    {
        Directory.SetCurrentDirectory(_tempDir);
        var cmd = new InitCommand(_interaction, _workloadManager);

        // No runtime specified, no workloads → prompt returns null (no runtimes available)
        var result = await cmd.RunInitAsync(
            workerRuntime: null, language: null, name: null, force: false,
            parseResult: null, CancellationToken.None);

        Assert.Equal(1, result);
        Assert.Contains(_interaction.Lines, l => l.Contains("No language workloads installed"));
    }

    [Fact]
    public async Task RunInitAsync_NoInitializerForRuntime_OffersInstall()
    {
        Directory.SetCurrentDirectory(_tempDir);
        var cmd = new InitCommand(_interaction, _workloadManager);

        // Specify a runtime but no workload is installed for it
        var result = await cmd.RunInitAsync(
            workerRuntime: "dotnet", language: null, name: null, force: false,
            parseResult: null, CancellationToken.None);

        Assert.Equal(1, result);
        Assert.Contains(_interaction.Lines, l => l.Contains("No workload installed"));
    }

    [Fact]
    public async Task RunInitAsync_ExistingProject_WithoutForce_ReturnsError()
    {
        Directory.SetCurrentDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "test.csproj"), "<Project/>");

        var cmd = new InitCommand(_interaction, _workloadManager);
        var result = await cmd.RunInitAsync(
            workerRuntime: "dotnet", language: null, name: null, force: false,
            parseResult: null, CancellationToken.None);

        Assert.Equal(1, result);
        Assert.Contains(_interaction.Lines, l => l.Contains("already contains"));
    }

    [Fact]
    public async Task RunInitAsync_ExistingProject_WithForce_CleansFiles()
    {
        Directory.SetCurrentDirectory(_tempDir);
        // Create files that would be cleaned
        File.WriteAllText(Path.Combine(_tempDir, "host.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "local.settings.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "MyApp.csproj"), "<Project/>");

        var cmd = new InitCommand(_interaction, _workloadManager);
        // Force=true but no workload installed for dotnet
        var result = await cmd.RunInitAsync(
            workerRuntime: "dotnet", language: null, name: null, force: true,
            parseResult: null, CancellationToken.None);

        // Files should be cleaned even though init ultimately fails (no workload)
        Assert.False(File.Exists(Path.Combine(_tempDir, "host.json")));
        Assert.False(File.Exists(Path.Combine(_tempDir, "local.settings.json")));
        Assert.False(File.Exists(Path.Combine(_tempDir, "MyApp.csproj")));
        Assert.Contains(_interaction.Lines, l => l.Contains("Cleaned existing project files"));
    }

    [Fact]
    public async Task RunInitAsync_Force_CleansProjectFilesByExtension()
    {
        Directory.SetCurrentDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "A.csproj"), "<Project/>");
        File.WriteAllText(Path.Combine(_tempDir, "B.fsproj"), "<Project/>");
        File.WriteAllText(Path.Combine(_tempDir, "userfile.txt"), "keep me");

        var cmd = new InitCommand(_interaction, _workloadManager);
        await cmd.RunInitAsync(
            workerRuntime: "dotnet", language: null, name: null, force: true,
            parseResult: null, CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(_tempDir, "A.csproj")));
        Assert.False(File.Exists(Path.Combine(_tempDir, "B.fsproj")));
        // User files should be preserved
        Assert.True(File.Exists(Path.Combine(_tempDir, "userfile.txt")));
    }

    [Fact]
    public async Task RunInitAsync_Force_CleansPropertiesDir()
    {
        Directory.SetCurrentDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "test.csproj"), "<Project/>");
        var propsDir = Path.Combine(_tempDir, "Properties");
        Directory.CreateDirectory(propsDir);
        File.WriteAllText(Path.Combine(propsDir, "launchSettings.json"), "{}");

        var cmd = new InitCommand(_interaction, _workloadManager);
        await cmd.RunInitAsync(
            workerRuntime: "dotnet", language: null, name: null, force: true,
            parseResult: null, CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(propsDir, "launchSettings.json")));
        // Properties dir should be removed if empty
        Assert.False(Directory.Exists(propsDir));
    }

    [Fact]
    public async Task RunInitAsync_Force_KeepsPropertiesDirIfNotEmpty()
    {
        Directory.SetCurrentDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "test.csproj"), "<Project/>");
        var propsDir = Path.Combine(_tempDir, "Properties");
        Directory.CreateDirectory(propsDir);
        File.WriteAllText(Path.Combine(propsDir, "launchSettings.json"), "{}");
        File.WriteAllText(Path.Combine(propsDir, "other.json"), "{}");

        var cmd = new InitCommand(_interaction, _workloadManager);
        await cmd.RunInitAsync(
            workerRuntime: "dotnet", language: null, name: null, force: true,
            parseResult: null, CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(propsDir, "launchSettings.json")));
        // Properties dir should remain because other.json is still there
        Assert.True(Directory.Exists(propsDir));
        Assert.True(File.Exists(Path.Combine(propsDir, "other.json")));
    }

    [Fact]
    public void InitCommand_UpdatesRuntimeDescription_NoWorkloads()
    {
        var cmd = new InitCommand(_interaction, _workloadManager);
        // With no workloads, description should still mention worker runtime
        Assert.Contains("worker runtime", InitCommand.WorkerRuntimeOption.Description);
    }
}
