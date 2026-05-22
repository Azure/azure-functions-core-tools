// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;
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
        RootCommand root = [];
        IReadOnlyList<Option> options = new GoProjectInitializer().GetInitOptions(new InitOptionRegistry(root));
        IReadOnlyList<string> names = [.. options.Select(o => o.Name)];

        Assert.Contains("--skip-go-mod-tidy", names);
        Assert.Contains("--no-bundles", names);
        Assert.Contains("--bundles-channel", names);
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
    public async Task InitializeAsync_AppendsExtensionBundle_IntoExistingHostJson()
    {
        File.WriteAllText(Path.Combine(_projectDir.FullName, "host.json"), "{ \"version\": \"2.0\" }");

        await RunAsync(projectName: "my-go-app", force: false);

        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "host.json")));
        Assert.NotNull(root);
        Assert.Equal("2.0", root!["version"]!.GetValue<string>());
        JsonNode? bundle = root["extensionBundle"];
        Assert.NotNull(bundle);
        Assert.Equal("Microsoft.Azure.Functions.ExtensionBundle", bundle!["id"]!.GetValue<string>());
        Assert.Equal("[4.*, 5.0.0)", bundle["version"]!.GetValue<string>());
    }

    [Fact]
    public async Task InitializeAsync_LeavesExistingExtensionBundleUntouched()
    {
        const string custom = "{\"version\":\"2.0\",\"extensionBundle\":{\"id\":\"custom\",\"version\":\"1.0.0\"}}";
        File.WriteAllText(Path.Combine(_projectDir.FullName, "host.json"), custom);

        await RunAsync(projectName: "my-go-app", force: false);

        JsonNode? bundle = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "host.json")))!["extensionBundle"];
        Assert.Equal("custom", bundle!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task InitializeAsync_BundlesChannel_Default_WritesDefaultBundleId()
    {
        await RunAsync(projectName: "my-go-app", force: false);

        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "host.json")));
        Assert.Equal(
            "Microsoft.Azure.Functions.ExtensionBundle",
            root!["extensionBundle"]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task InitializeAsync_BundlesChannel_Preview_WritesPreviewBundleId()
    {
        await RunAsync(projectName: "my-go-app", force: false, args: ["--bundles-channel", "Preview"]);

        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "host.json")));
        Assert.Equal(
            "Microsoft.Azure.Functions.ExtensionBundle.Preview",
            root!["extensionBundle"]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task InitializeAsync_BundlesChannel_Experimental_WritesExperimentalBundleId()
    {
        await RunAsync(projectName: "my-go-app", force: false, args: ["--bundles-channel", "Experimental"]);

        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "host.json")));
        Assert.Equal(
            "Microsoft.Azure.Functions.ExtensionBundle.Experimental",
            root!["extensionBundle"]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task InitializeAsync_NoBundle_WritesMinimalHostJsonWithoutExtensionBundle()
    {
        await RunAsync(projectName: "my-go-app", force: false, args: ["--no-bundles"]);

        string hostJsonPath = Path.Combine(_projectDir.FullName, "host.json");
        Assert.True(File.Exists(hostJsonPath), "host.json should be created even with --no-bundles");
        string content = File.ReadAllText(hostJsonPath);
        Assert.Contains("\"version\"", content);
        Assert.DoesNotContain("extensionBundle", content);
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
        initializer.GetInitOptions(new InitOptionRegistry(root));

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
        initializer.GetInitOptions(new InitOptionRegistry(root));

        await initializer.InitializeAsync(context, root.Parse(string.Empty));
        Assert.Equal(1, calls);
    }

    private Task RunAsync(string? projectName, bool force, string[]? args = null)
    {
        var initializer = new GoProjectInitializer();
        // Skip the real `go mod tidy` invocation in tests by stubbing the seam.
        initializer.RunGoModTidy = (_, _) => Task.FromResult((0, string.Empty));
        var context = new InitContext(
            WorkingDirectory.FromExplicit(_projectDir.FullName),
            ProjectName: projectName,
            Language: null,
            Force: force);
        var root = new RootCommand();
        initializer.GetInitOptions(new InitOptionRegistry(root));

        return initializer.InitializeAsync(context, root.Parse(args ?? []));
    }
}
