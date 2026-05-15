// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Commands;
using Xunit;

namespace Azure.Functions.Cli.Workloads.Node.Tests;

public class NodeProjectInitializerTests : IDisposable
{
    private readonly DirectoryInfo _projectDir;

    public NodeProjectInitializerTests()
    {
        _projectDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "func-node-init-" + Guid.NewGuid().ToString("N")));
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
    public void Stack_IsNode()
    {
        Assert.Equal("node", new NodeProjectInitializer().Stack);
    }

    [Fact]
    public void GetInitOptions_IsEmpty()
    {
        Assert.Empty(new NodeProjectInitializer().GetInitOptions());
    }

    [Fact]
    public async Task InitializeAsync_DefaultsToJavaScript()
    {
        await RunAsync(language: null, force: false);

        Assert.True(File.Exists(Path.Combine(_projectDir.FullName, "package.json")));
        Assert.False(File.Exists(Path.Combine(_projectDir.FullName, "tsconfig.json")));

        using var pkg = JsonDocument.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "package.json")));
        Assert.Equal("src/functions/*.js", pkg.RootElement.GetProperty("main").GetString());
    }

    [Fact]
    public async Task InitializeAsync_TypeScript_WritesTsConfigAndTsPackageJson()
    {
        await RunAsync(language: "typescript", force: false);

        Assert.True(File.Exists(Path.Combine(_projectDir.FullName, "tsconfig.json")));
        using var pkg = JsonDocument.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "package.json")));
        Assert.Contains("tsc", pkg.RootElement.GetProperty("scripts").GetProperty("build").GetString());
    }

    [Fact]
    public async Task InitializeAsync_SubstitutesProjectName()
    {
        await RunAsync(language: null, force: false, projectName: "My Cool App");

        using var pkg = JsonDocument.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "package.json")));
        Assert.Equal("my-cool-app", pkg.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task InitializeAsync_WritesCommonScaffolding()
    {
        await RunAsync(language: null, force: false);

        Assert.True(File.Exists(Path.Combine(_projectDir.FullName, ".funcignore")));
        Assert.True(File.Exists(Path.Combine(_projectDir.FullName, ".gitignore")));
        Assert.True(Directory.Exists(Path.Combine(_projectDir.FullName, "src", "functions")));

        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "local.settings.json")));
        Assert.Equal("node", settings.RootElement.GetProperty("Values").GetProperty("FUNCTIONS_WORKER_RUNTIME").GetString());
    }

    [Fact]
    public async Task InitializeAsync_AppendsExtensionBundle_IntoExistingHostJson()
    {
        File.WriteAllText(Path.Combine(_projectDir.FullName, "host.json"), "{ \"version\": \"2.0\" }");

        await RunAsync(language: null, force: false);

        JsonNode? bundle = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "host.json")))!["extensionBundle"];
        Assert.NotNull(bundle);
        Assert.Equal("Microsoft.Azure.Functions.ExtensionBundle", bundle!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent_WithoutForce()
    {
        await RunAsync(language: null, force: false);
        const string userEdit = "{ \"name\": \"user-edited\" }";
        File.WriteAllText(Path.Combine(_projectDir.FullName, "package.json"), userEdit);

        await RunAsync(language: null, force: false);

        Assert.Equal(userEdit, File.ReadAllText(Path.Combine(_projectDir.FullName, "package.json")));
    }

    [Fact]
    public async Task InitializeAsync_ForceOverwrites()
    {
        await RunAsync(language: null, force: false);
        File.WriteAllText(Path.Combine(_projectDir.FullName, "package.json"), "stale");

        await RunAsync(language: null, force: true);

        Assert.Contains("@azure/functions", File.ReadAllText(Path.Combine(_projectDir.FullName, "package.json")));
    }

    private Task RunAsync(string? language, bool force, string? projectName = "test")
    {
        NodeProjectInitializer initializer = new();
        InitContext context = new(
            WorkingDirectory.FromExplicit(_projectDir.FullName),
            ProjectName: projectName,
            Language: language,
            Force: force);
        return initializer.InitializeAsync(context, new RootCommand().Parse(string.Empty));
    }
}
