// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;
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

    [Theory]
    [InlineData(null, "JavaScript")]
    [InlineData("", "JavaScript")]
    [InlineData("js", "JavaScript")]
    [InlineData("JS", "JavaScript")]
    [InlineData("JavaScript", "JavaScript")]
    [InlineData("ts", "TypeScript")]
    [InlineData("TS", "TypeScript")]
    [InlineData("TypeScript", "TypeScript")]
    [InlineData("unknown", "unknown")]
    public void NormalizeLanguage_ReturnsExpectedResult(string? input, string expected)
    {
        Assert.Equal(expected, new NodeProjectInitializer().NormalizeLanguage(input));
    }

    [Fact]
    public async Task InitializeAsync_UnsupportedLanguage_Throws()
    {
        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => RunAsync(language: "ruby", force: false));

        Assert.Contains("ruby", ex.Message);
    }

    [Fact]
    public async Task InitializeAsync_AliasTs_ScaffoldsTypeScript()
    {
        await RunAsync(language: "ts", force: false);

        Assert.True(File.Exists(Path.Combine(_projectDir.FullName, "tsconfig.json")));
    }

    [Fact]
    public async Task InitializeAsync_AliasJs_ScaffoldsJavaScript()
    {
        await RunAsync(language: "js", force: false);

        Assert.True(File.Exists(Path.Combine(_projectDir.FullName, "package.json")));
        Assert.False(File.Exists(Path.Combine(_projectDir.FullName, "tsconfig.json")));
    }

    [Fact]
    public void GetInitOptions_RegistersBundleAndNpmOptions()
    {
        RootCommand root = [];
        IReadOnlyList<Option> options = new NodeProjectInitializer().GetInitOptions(new InitOptionRegistry(root));
        IReadOnlyList<string> names = [.. options.Select(o => o.Name)];

        Assert.Contains("--no-bundles", names);
        Assert.Contains("--bundles-channel", names);
        Assert.Contains("--skip-npm-install", names);
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

    [Fact]
    public async Task InitializeAsync_NoBundle_WritesMinimalHostJsonWithoutExtensionBundle()
    {
        await RunAsync(language: null, force: false, args: ["--no-bundles"]);

        string hostJsonPath = Path.Combine(_projectDir.FullName, "host.json");
        Assert.True(File.Exists(hostJsonPath), "host.json should be created even with --no-bundles");
        string content = File.ReadAllText(hostJsonPath);
        Assert.Contains("\"version\"", content);
        Assert.DoesNotContain("extensionBundle", content);
    }

    [Fact]
    public async Task InitializeAsync_BundlesChannel_Preview_WritesPreviewBundleId()
    {
        await RunAsync(language: null, force: false, args: ["--bundles-channel", "Preview"]);

        JsonNode? bundle = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "host.json")))!["extensionBundle"];
        Assert.Equal("Microsoft.Azure.Functions.ExtensionBundle.Preview", bundle!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task InitializeAsync_RunsNpmInstallByDefault()
    {
        int npmCalls = 0;
        await RunAsync(language: null, force: false, configure: init =>
        {
            init.RunNpmInstall = (_, _) =>
            {
                npmCalls++;
                return Task.FromResult((0, string.Empty));
            };
        });

        Assert.Equal(1, npmCalls);
    }

    [Fact]
    public async Task InitializeAsync_SkipNpmInstall_DoesNotInvokeNpm()
    {
        int npmCalls = 0;
        await RunAsync(language: null, force: false, args: ["--skip-npm-install"], configure: init =>
        {
            init.RunNpmInstall = (_, _) =>
            {
                npmCalls++;
                return Task.FromResult((0, string.Empty));
            };
        });

        Assert.Equal(0, npmCalls);
    }

    [Fact]
    public async Task InitializeAsync_NpmInstallFailure_DoesNotThrow()
    {
        await RunAsync(language: null, force: false, configure: init =>
        {
            init.RunNpmInstall = (_, _) => Task.FromResult((1, "npm: command not found"));
        });

        Assert.True(File.Exists(Path.Combine(_projectDir.FullName, "package.json")));
    }

    private Task RunAsync(
        string? language,
        bool force,
        string? projectName = "test",
        string[]? args = null,
        Action<NodeProjectInitializer>? configure = null)
    {
        NodeProjectInitializer initializer = new()
        {
            // Stub by default so tests don't spawn real npm.
            RunNpmInstall = (_, _) => Task.FromResult((0, string.Empty)),
        };
        configure?.Invoke(initializer);

        InitContext context = new(
            WorkingDirectory.FromExplicit(_projectDir.FullName),
            ProjectName: projectName,
            Language: language,
            Force: force);

        RootCommand root = [];
        var registry = new InitOptionRegistry(root);
        initializer.GetInitOptions(registry);

        ParseResult parseResult = root.Parse(args ?? []);
        return initializer.InitializeAsync(context, parseResult);
    }
}
