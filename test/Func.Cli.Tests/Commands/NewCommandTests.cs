// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Workloads;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

[Collection(nameof(WorkingDirectoryTests))]
public class NewCommandTests : IDisposable
{
    private static readonly string _safeDir = Path.GetTempPath();
    private readonly string _tempDir;
    private readonly TestInteractionService _interaction;
    private readonly WorkloadManager _workloadManager;

    public NewCommandTests()
    {
        Directory.SetCurrentDirectory(_safeDir);
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-new-{Guid.NewGuid():N}");
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
    public void NewCommand_HasExpectedOptions()
    {
        var cmd = new NewCommand(_interaction);
        var optionNames = cmd.Options.Select(o => o.Name).ToList();

        Assert.Contains("--name", optionNames);
        Assert.Contains("--template", optionNames);
        Assert.Contains("--force", optionNames);
    }

    [Fact]
    public void NewCommand_RegisteredInParser()
    {
        var root = Parser.CreateCommand(_interaction);
        var names = root.Subcommands.Select(c => c.Name).ToList();

        Assert.Contains("new", names);
    }

    [Fact]
    public async Task ExecuteAsync_NoHostJson_NonInteractive_ReturnsError()
    {
        // TestInteractionService.IsInteractive = false, so it won't prompt for init
        var projectDir = Path.Combine(_tempDir, "empty-project");
        Directory.CreateDirectory(projectDir);

        var cmd = new NewCommand(_interaction, _workloadManager);
        var parseResult = cmd.Parse([projectDir]);

        var exitCode = await parseResult.InvokeAsync();

        Assert.Equal(1, exitCode);
        Assert.Contains(_interaction.Lines, l => l.Contains("No Azure Functions project found"));
    }

    [Fact]
    public async Task ExecuteAsync_NoWorkloads_ReturnsError()
    {
        // Create project dir with host.json but no workloads installed
        var projectDir = Path.Combine(_tempDir, "has-host");
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "host.json"), "{}");

        var cmd = new NewCommand(_interaction, _workloadManager);
        var parseResult = cmd.Parse([projectDir]);

        var exitCode = await parseResult.InvokeAsync();

        Assert.Equal(1, exitCode);
        Assert.Contains(_interaction.Lines, l => l.Contains("No language workloads installed"));
    }

    [Fact]
    public async Task ExecuteAsync_NoTemplatesAvailable_ReturnsError()
    {
        // host.json exists, workload is installed but has no templates
        // (WorkloadManager with no actual assemblies loaded won't have templates)
        var projectDir = Path.Combine(_tempDir, "no-templates");
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "host.json"), "{}");
        File.WriteAllText(Path.Combine(projectDir, "test.csproj"), "<Project/>");

        var cmd = new NewCommand(_interaction, _workloadManager);
        var parseResult = cmd.Parse([projectDir]);

        var exitCode = await parseResult.InvokeAsync();

        // With no loaded workloads, we get "no workloads installed" or "no templates"
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_TemplateNotFound_ReturnsError()
    {
        var projectDir = Path.Combine(_tempDir, "bad-template");
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "host.json"), "{}");

        var cmd = new NewCommand(_interaction, _workloadManager);
        var parseResult = cmd.Parse(["--template", "NonExistentTemplate", projectDir]);

        var exitCode = await parseResult.InvokeAsync();

        Assert.Equal(1, exitCode);
    }
}
