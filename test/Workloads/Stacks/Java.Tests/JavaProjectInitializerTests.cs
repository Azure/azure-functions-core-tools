// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;
using Xunit;

namespace Azure.Functions.Cli.Workloads.Java.Tests;

public class JavaProjectInitializerTests : IDisposable
{
    private readonly DirectoryInfo _projectDir;

    public JavaProjectInitializerTests()
    {
        _projectDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "func-java-init-" + Guid.NewGuid().ToString("N")));
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
    public void Stack_IsJava()
    {
        Assert.Equal("java", new JavaProjectInitializer().Stack);
    }

    [Fact]
    public void GetInitOptions_ContainsBundleOptions()
    {
        RootCommand root = [];
        IReadOnlyList<Option> options = new JavaProjectInitializer().GetInitOptions(new InitOptionRegistry(root));
        IReadOnlyList<string> names = [.. options.Select(o => o.Name)];

        Assert.Contains("--no-bundles", names);
        Assert.Contains("--bundles-channel", names);
    }

    [Fact]
    public async Task InitializeAsync_WritesExpectedFiles()
    {
        await RunAsync(projectName: "my-java-app", force: false);

        string pom = File.ReadAllText(Path.Combine(_projectDir.FullName, "pom.xml"));
        Assert.Contains("<artifactId>my-java-app</artifactId>", pom);
        Assert.Contains("azure-functions-java-library", pom);
        Assert.Contains("azure-functions-maven-plugin", pom);
        Assert.Contains("<functionAppName>my-java-app</functionAppName>", pom);
        Assert.Contains("<pricingTier>Flex Consumption</pricingTier>", pom);
        Assert.Contains("<javaVersion>21</javaVersion>", pom);

        string functionJava = File.ReadAllText(
            Path.Combine(_projectDir.FullName, "src", "main", "java", "com", "function", "Function.java"));
        Assert.Contains("package com.function;", functionJava);
        Assert.Contains("@FunctionName", functionJava);

        Assert.True(File.Exists(Path.Combine(_projectDir.FullName, ".funcignore")));
        Assert.True(File.Exists(Path.Combine(_projectDir.FullName, ".gitignore")));

        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "local.settings.json")));
        Assert.Equal("java", settings.RootElement.GetProperty("Values").GetProperty("FUNCTIONS_WORKER_RUNTIME").GetString());
    }

    [Fact]
    public async Task InitializeAsync_FallsBackToDirectoryNameForArtifactId()
    {
        await RunAsync(projectName: null, force: false);

        string pom = File.ReadAllText(Path.Combine(_projectDir.FullName, "pom.xml"));
        Assert.Matches(@"<artifactId>\S+</artifactId>", pom);
        Assert.DoesNotContain("{ArtifactId}", pom);
        Assert.DoesNotContain("{FunctionAppName}", pom);
    }

    [Fact]
    public async Task InitializeAsync_AppendsExtensionBundle_IntoExistingHostJson()
    {
        File.WriteAllText(Path.Combine(_projectDir.FullName, "host.json"), "{ \"version\": \"2.0\" }");

        await RunAsync(projectName: "my-java-app", force: false);

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

        await RunAsync(projectName: "my-java-app", force: false);

        JsonNode? bundle = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "host.json")))!["extensionBundle"];
        Assert.Equal("custom", bundle!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task InitializeAsync_BundlesChannel_Preview_WritesPreviewBundleId()
    {
        await RunAsync(projectName: "my-java-app", force: false, args: ["--bundles-channel", "Preview"]);

        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "host.json")));
        Assert.Equal(
            "Microsoft.Azure.Functions.ExtensionBundle.Preview",
            root!["extensionBundle"]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task InitializeAsync_NoBundle_WritesMinimalHostJsonWithoutExtensionBundle()
    {
        await RunAsync(projectName: "my-java-app", force: false, args: ["--no-bundles"]);

        string hostJsonPath = Path.Combine(_projectDir.FullName, "host.json");
        Assert.True(File.Exists(hostJsonPath), "host.json should be created even with --no-bundles");
        string content = File.ReadAllText(hostJsonPath);
        Assert.Contains("\"version\"", content);
        Assert.DoesNotContain("extensionBundle", content);
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent_WithoutForce()
    {
        await RunAsync(projectName: "my-java-app", force: false);
        string functionPath = Path.Combine(_projectDir.FullName, "src", "main", "java", "com", "function", "Function.java");
        const string userEdit = "package com.function;\npublic class Function {}\n";
        File.WriteAllText(functionPath, userEdit);

        await RunAsync(projectName: "my-java-app", force: false);

        Assert.Equal(userEdit, File.ReadAllText(functionPath));
    }

    [Fact]
    public async Task InitializeAsync_ForceOverwrites()
    {
        await RunAsync(projectName: "my-java-app", force: false);
        string pomPath = Path.Combine(_projectDir.FullName, "pom.xml");
        File.WriteAllText(pomPath, "stale");

        await RunAsync(projectName: "my-java-app", force: true);

        Assert.Contains("azure-functions-maven-plugin", File.ReadAllText(pomPath));
    }

    private Task RunAsync(string? projectName, bool force, string[]? args = null)
    {
        var initializer = new JavaProjectInitializer();
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
