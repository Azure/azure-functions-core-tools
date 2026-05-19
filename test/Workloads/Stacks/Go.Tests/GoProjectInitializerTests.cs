// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Commands;
using Xunit;

namespace Azure.Functions.Cli.Workloads.Go.Tests;

public class GoProjectInitializerTests : IDisposable
{
    private readonly DirectoryInfo _projectDir;

    public GoProjectInitializerTests()
    {
        _projectDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "func-go-init-" + Guid.NewGuid().ToString("N")));
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
    public void Stack_IsGo()
    {
        Assert.Equal("go", new GoProjectInitializer().Stack);
    }

    [Fact]
    public void GetInitOptions_ContainsSkipGoModTidy()
    {
        IReadOnlyList<Option> options = new GoProjectInitializer().GetInitOptions();
        Assert.Single(options);
        Assert.Equal("--skip-go-mod-tidy", options[0].Name);
    }

    [Fact]
    public async Task InitializeAsync_WritesExpectedFiles()
    {
        await RunAsync(projectName: "my-go-app", force: false);

        string goMod = File.ReadAllText(Path.Combine(_projectDir.FullName, "go.mod"));
        Assert.Contains("module my-go-app", goMod);
        Assert.Contains("go 1.24", goMod);
        Assert.Contains("github.com/azure/azure-functions-golang-worker", goMod);

        string mainGo = File.ReadAllText(Path.Combine(_projectDir.FullName, "main.go"));
        Assert.Contains("github.com/azure/azure-functions-golang-worker/sdk", mainGo);
        Assert.Contains("worker.Start", mainGo);

        Assert.True(File.Exists(Path.Combine(_projectDir.FullName, ".funcignore")));
        Assert.True(File.Exists(Path.Combine(_projectDir.FullName, ".gitignore")));

        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "local.settings.json")));
        Assert.Equal("native", settings.RootElement.GetProperty("Values").GetProperty("FUNCTIONS_WORKER_RUNTIME").GetString());
    }

    [Fact]
    public async Task InitializeAsync_FallsBackToDirectoryNameForModule()
    {
        await RunAsync(projectName: null, force: false);

        string goMod = File.ReadAllText(Path.Combine(_projectDir.FullName, "go.mod"));
        Assert.Matches(@"^module \S+", goMod);
    }

    [Fact]
    public async Task InitializeAsync_DoesNotTouchHostJson()
    {
        const string baseHost = "{\n  \"version\": \"2.0\"\n}";
        File.WriteAllText(Path.Combine(_projectDir.FullName, "host.json"), baseHost);

        await RunAsync(projectName: "my-go-app", force: false);

        string after = File.ReadAllText(Path.Combine(_projectDir.FullName, "host.json"));
        Assert.Equal(baseHost, after);
        var root = JsonNode.Parse(after) as JsonObject;
        Assert.NotNull(root);
        Assert.False(root!.ContainsKey("customHandler"));
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent_WithoutForce()
    {
        await RunAsync(projectName: "my-go-app", force: false);
        const string userEdit = "package main\nfunc main() {}\n";
        File.WriteAllText(Path.Combine(_projectDir.FullName, "main.go"), userEdit);

        await RunAsync(projectName: "my-go-app", force: false);

        Assert.Equal(userEdit, File.ReadAllText(Path.Combine(_projectDir.FullName, "main.go")));
    }

    [Fact]
    public async Task InitializeAsync_ForceOverwrites()
    {
        await RunAsync(projectName: "my-go-app", force: false);
        File.WriteAllText(Path.Combine(_projectDir.FullName, "main.go"), "stale");

        await RunAsync(projectName: "my-go-app", force: true);

        Assert.Contains("worker.Start", File.ReadAllText(Path.Combine(_projectDir.FullName, "main.go")));
    }

    [Fact]
    public async Task InitializeAsync_SkipsGoModTidy_WhenFlagSet()
    {
        var initializer = new GoProjectInitializer();
        int calls = 0;
        initializer.RunGoModTidy = (_, _) => { calls++; return Task.FromResult((0, string.Empty)); };

        var context = new InitContext(
            WorkingDirectory.FromExplicit(_projectDir.FullName),
            ProjectName: "my-go-app",
            Language: null,
            Force: false);
        var root = new RootCommand();
        foreach (Option opt in initializer.GetInitOptions())
        {
            root.Options.Add(opt);
        }

        await initializer.InitializeAsync(context, root.Parse("--skip-go-mod-tidy"));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task InitializeAsync_RunsGoModTidy_ByDefault()
    {
        var initializer = new GoProjectInitializer();
        int calls = 0;
        initializer.RunGoModTidy = (_, _) => { calls++; return Task.FromResult((0, string.Empty)); };

        var context = new InitContext(
            WorkingDirectory.FromExplicit(_projectDir.FullName),
            ProjectName: "my-go-app",
            Language: null,
            Force: false);
        var root = new RootCommand();
        foreach (Option opt in initializer.GetInitOptions())
        {
            root.Options.Add(opt);
        }

        await initializer.InitializeAsync(context, root.Parse(string.Empty));
        Assert.Equal(1, calls);
    }

    private Task RunAsync(string? projectName, bool force)
    {
        var initializer = new GoProjectInitializer();
        // Skip the real `go mod tidy` invocation in tests by stubbing the seam.
        initializer.RunGoModTidy = (_, _) => Task.FromResult((0, string.Empty));
        var context = new InitContext(
            WorkingDirectory.FromExplicit(_projectDir.FullName),
            ProjectName: projectName,
            Language: null,
            Force: force);
        return initializer.InitializeAsync(context, new RootCommand().Parse(string.Empty));
    }
}
