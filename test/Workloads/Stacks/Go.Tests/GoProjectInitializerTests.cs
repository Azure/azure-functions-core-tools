// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Go.Tests;

public class GoProjectInitializerTests : IDisposable
{
    [Fact]
    public void Constructor_NullStack_Throws()
    {
        FluentActions.Invoking(() => new GoProjectInitializer(null!))
            .Should().ThrowExactly<ArgumentNullException>().WithParameterName("stack");
    }

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
        new GoProjectInitializer(new GoProjectStack()).Stack.Should().Be("go");
    }

    [Fact]
    public void GetInitOptions_ContainsSkipGoModTidy()
    {
        RootCommand root = [];
        IReadOnlyList<Option> options = new GoProjectInitializer(new GoProjectStack()).GetInitOptions(new InitOptionRegistry(root));
        IReadOnlyList<string> names = [.. options.Select(o => o.Name)];

        names.Should().Contain("--skip-go-mod-tidy");
        names.Should().Contain("--no-bundles");
        names.Should().Contain("--bundles-channel");
    }

    [Fact]
    public async Task InitializeAsync_WritesExpectedFiles()
    {
        await RunAsync(projectName: "my-go-app", force: false);

        string goMod = File.ReadAllText(Path.Combine(_projectDir.FullName, "go.mod"));
        goMod.Should().Contain("module my-go-app");
        goMod.Should().Contain("go 1.24");
        // go.mod intentionally omits a `require` line; `go mod tidy` resolves it from main.go imports.
        goMod.Should().NotContain("require");

        string mainGo = File.ReadAllText(Path.Combine(_projectDir.FullName, "main.go"));
        mainGo.Should().Contain("github.com/azure/azure-functions-golang-worker/sdk");
        mainGo.Should().Contain("worker.Start");

        File.Exists(Path.Combine(_projectDir.FullName, ".funcignore")).Should().BeTrue();
        File.Exists(Path.Combine(_projectDir.FullName, ".gitignore")).Should().BeTrue();

        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "local.settings.json")));
        settings.RootElement.GetProperty("Values").GetProperty("FUNCTIONS_WORKER_RUNTIME").GetString().Should().Be("native");
    }

    [Fact]
    public async Task InitializeAsync_FallsBackToDirectoryNameForModule()
    {
        await RunAsync(projectName: null, force: false);

        string goMod = File.ReadAllText(Path.Combine(_projectDir.FullName, "go.mod"));
        goMod.Should().MatchRegex(@"^module \S+");
    }

    [Theory]
    [InlineData("go")]
    [InlineData("toolchain")]
    public async Task InitializeAsync_ReservedModuleName_UsesValidFallback(string name)
    {
        await RunAsync(projectName: name, force: false);

        File.ReadAllText(Path.Combine(_projectDir.FullName, "go.mod")).Should().StartWith("module app");
    }

    [Theory]
    [InlineData(" M\u00c9 App_42! ", "m-app_42")]
    [InlineData("\u00c9\u4e2d\u6587\u0661", "app")]
    [InlineData("App\u0661\u0662\u0663_42", "app_42")]
    [InlineData("\ud83d\ude80My App", "my-app")]
    [InlineData("\u00c9-app", "app")]
    [InlineData("-my-app", "app")]
    [InlineData("---", "app")]
    [InlineData("CON", "app")]
    [InlineData("prn", "app")]
    [InlineData("AuX", "app")]
    [InlineData("nul", "app")]
    [InlineData("COM1", "app")]
    [InlineData("COM9", "app")]
    [InlineData("LPT1", "app")]
    [InlineData("LPT9", "app")]
    [InlineData(" My App_42! ", "my-app_42")]
    [InlineData("APP", "app")]
    [InlineData("!!!", "app")]
    [InlineData("GO", "app")]
    [InlineData("TOOLCHAIN", "app")]
    [InlineData("123", "123")]
    [InlineData("_", "_")]
    [InlineData("_my-app_", "_my-app_")]
    [InlineData("app--", "app--")]
    [InlineData("com0", "com0")]
    [InlineData("com10", "com10")]
    [InlineData("lpt0", "lpt0")]
    [InlineData("lpt10", "lpt10")]
    [InlineData("con-app", "con-app")]
    public async Task InitializeAsync_ProjectName_DerivesPortableModuleName(string name, string expected)
    {
        await RunAsync(projectName: name, force: false);

        File.ReadLines(Path.Combine(_projectDir.FullName, "go.mod")).First().Should().Be("module " + expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task InitializeAsync_OmittedName_UsesPortableUnicodeDirectoryName(string? name)
    {
        string directory = Directory.CreateDirectory(Path.Combine(_projectDir.FullName, "M\u00c9 Project")).FullName;
        var initializer = new GoProjectInitializer(new GoProjectStack());
        var root = new RootCommand();
        initializer.GetInitOptions(new InitOptionRegistry(root));
        var context = new InitContext(WorkingDirectory.FromExplicit(directory), name, Language: null, Force: false);

        await initializer.InitializeAsync(context, root.Parse("--skip-go-mod-tidy"), CancellationToken.None);

        File.ReadLines(Path.Combine(directory, "go.mod")).First().Should().Be("module m-project");
    }

    [Fact]
    public async Task InitializeAsync_AppendsExtensionBundle_IntoExistingHostJson()
    {
        File.WriteAllText(Path.Combine(_projectDir.FullName, "host.json"), "{ \"version\": \"2.0\" }");

        await RunAsync(projectName: "my-go-app", force: false);

        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "host.json")));
        root.Should().NotBeNull();
        root!["version"]!.GetValue<string>().Should().Be("2.0");
        JsonNode? bundle = root["extensionBundle"];
        bundle.Should().NotBeNull();
        bundle!["id"]!.GetValue<string>().Should().Be("Microsoft.Azure.Functions.ExtensionBundle");
        bundle["version"]!.GetValue<string>().Should().Be("[4.*, 5.0.0)");
    }

    [Fact]
    public async Task InitializeAsync_LeavesExistingExtensionBundleUntouched()
    {
        const string custom = "{\"version\":\"2.0\",\"extensionBundle\":{\"id\":\"custom\",\"version\":\"1.0.0\"}}";
        File.WriteAllText(Path.Combine(_projectDir.FullName, "host.json"), custom);

        await RunAsync(projectName: "my-go-app", force: false);

        JsonNode? bundle = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "host.json")))!["extensionBundle"];
        bundle!["id"]!.GetValue<string>().Should().Be("custom");
    }

    [Fact]
    public async Task InitializeAsync_BundlesChannel_Default_WritesDefaultBundleId()
    {
        await RunAsync(projectName: "my-go-app", force: false);

        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "host.json")));
        root!["extensionBundle"]!["id"]!.GetValue<string>().Should().Be("Microsoft.Azure.Functions.ExtensionBundle");
    }

    [Fact]
    public async Task InitializeAsync_BundlesChannel_Preview_WritesPreviewBundleId()
    {
        await RunAsync(projectName: "my-go-app", force: false, args: ["--bundles-channel", "Preview"]);

        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "host.json")));
        root!["extensionBundle"]!["id"]!.GetValue<string>().Should().Be("Microsoft.Azure.Functions.ExtensionBundle.Preview");
    }

    [Fact]
    public async Task InitializeAsync_BundlesChannel_Experimental_WritesExperimentalBundleId()
    {
        await RunAsync(projectName: "my-go-app", force: false, args: ["--bundles-channel", "Experimental"]);

        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "host.json")));
        root!["extensionBundle"]!["id"]!.GetValue<string>().Should().Be("Microsoft.Azure.Functions.ExtensionBundle.Experimental");
    }

    [Fact]
    public async Task InitializeAsync_NoBundle_WritesMinimalHostJsonWithoutExtensionBundle()
    {
        await RunAsync(projectName: "my-go-app", force: false, args: ["--no-bundles"]);

        string hostJsonPath = Path.Combine(_projectDir.FullName, "host.json");
        File.Exists(hostJsonPath).Should().BeTrue("host.json should be created even with --no-bundles");
        string content = File.ReadAllText(hostJsonPath);
        content.Should().Contain("\"version\"");
        content.Should().NotContain("extensionBundle");
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent_WithoutForce()
    {
        await RunAsync(projectName: "my-go-app", force: false);
        const string userEdit = "package main\nfunc main() {}\n";
        File.WriteAllText(Path.Combine(_projectDir.FullName, "main.go"), userEdit);

        await RunAsync(projectName: "my-go-app", force: false);

        File.ReadAllText(Path.Combine(_projectDir.FullName, "main.go")).Should().Be(userEdit);
    }

    [Fact]
    public async Task InitializeAsync_ForceOverwrites()
    {
        await RunAsync(projectName: "my-go-app", force: false);
        File.WriteAllText(Path.Combine(_projectDir.FullName, "main.go"), "stale");

        await RunAsync(projectName: "my-go-app", force: true);

        File.ReadAllText(Path.Combine(_projectDir.FullName, "main.go")).Should().Contain("worker.Start");
    }

    [Fact]
    public async Task InitializeAsync_SkipsGoModTidy_WhenFlagSet()
    {
        var initializer = new GoProjectInitializer(new GoProjectStack());
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
        calls.Should().Be(0);
    }

    [Fact]
    public async Task InitializeAsync_RunsGoModTidy_ByDefault()
    {
        var initializer = new GoProjectInitializer(new GoProjectStack());
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
        calls.Should().Be(1);
    }

    private Task RunAsync(string? projectName, bool force, string[]? args = null)
    {
        var initializer = new GoProjectInitializer(new GoProjectStack());
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
