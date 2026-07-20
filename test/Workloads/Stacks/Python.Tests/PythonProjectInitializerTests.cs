// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Python.Tests;

public class PythonProjectInitializerTests : IDisposable
{
    private readonly DirectoryInfo _projectDir;

    public PythonProjectInitializerTests()
    {
        _projectDir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "func-python-init-" + Guid.NewGuid().ToString("N")));
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
    public void Stack_IsPython()
    {
        new PythonProjectInitializer().Stack.Should().Be("python");
    }

    [Theory]
    [InlineData(null, "Python")]
    [InlineData("", "Python")]
    [InlineData("py", "Python")]
    [InlineData("PY", "Python")]
    [InlineData("Python", "Python")]
    [InlineData("PYTHON", "Python")]
    [InlineData("unknown", "unknown")]
    public void NormalizeLanguage_ReturnsExpectedResult(string? input, string expected)
    {
        new PythonProjectInitializer().NormalizeLanguage(input).Should().Be(expected);
    }

    [Fact]
    public async Task InitializeAsync_UnsupportedLanguage_Throws()
    {
        ArgumentException ex = (await FluentActions.Awaiting(() => RunAsync(force: false, language: "ruby")).Should().ThrowAsync<ArgumentException>()).Which;

        ex.Message.Should().Contain("ruby");
    }

    [Fact]
    public async Task InitializeAsync_AliasPy_Succeeds()
    {
        await RunAsync(force: false, language: "py");

        File.Exists(Path.Combine(_projectDir.FullName, "function_app.py")).Should().BeTrue();
    }

    [Fact]
    public void GetInitOptions_RegistersBundleOptions()
    {
        RootCommand root = [];
        IReadOnlyList<Option> options = new PythonProjectInitializer().GetInitOptions(new InitOptionRegistry(root));
        IReadOnlyList<string> names = [.. options.Select(o => o.Name)];

        names.Should().Contain("--no-bundles");
        names.Should().Contain("--bundles-channel");
    }

    [Fact]
    public async Task InitializeAsync_WritesExpectedFiles()
    {
        await RunAsync(force: false);

        File.Exists(Path.Combine(_projectDir.FullName, "function_app.py")).Should().BeTrue();
        File.ReadAllText(Path.Combine(_projectDir.FullName, "function_app.py")).Should().Contain("FunctionApp");

        File.Exists(Path.Combine(_projectDir.FullName, "requirements.txt")).Should().BeTrue();
        File.ReadAllText(Path.Combine(_projectDir.FullName, "requirements.txt")).Should().Contain("azure-functions");

        File.Exists(Path.Combine(_projectDir.FullName, "getting_started.md")).Should().BeTrue();
        File.Exists(Path.Combine(_projectDir.FullName, ".gitignore")).Should().BeTrue();

        File.Exists(Path.Combine(_projectDir.FullName, "local.settings.json")).Should().BeTrue();
        var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "local.settings.json")));
        settings.RootElement.GetProperty("Values").GetProperty("FUNCTIONS_WORKER_RUNTIME").GetString().Should().Be("python");
    }

    [Fact]
    public async Task InitializeAsync_AppendsExtensionBundle_IntoExistingHostJson()
    {
        // Pre-existing host.json with version only. WriteIfMissing won't
        // overwrite (force=false), and MergeHostJson adds extensionBundle
        // without dropping version.
        File.WriteAllText(Path.Combine(_projectDir.FullName, "host.json"), "{ \"version\": \"2.0\" }");

        await RunAsync(force: false);

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

        await RunAsync(force: false);

        JsonNode? bundle = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "host.json")))!["extensionBundle"];
        bundle!["id"]!.GetValue<string>().Should().Be("custom");
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent_WithoutForce()
    {
        // First run scaffolds; user-edited file must survive a second run.
        await RunAsync(force: false);
        const string userEdit = "import azure.functions as func\n# user edits\n";
        File.WriteAllText(Path.Combine(_projectDir.FullName, "function_app.py"), userEdit);

        await RunAsync(force: false);

        File.ReadAllText(Path.Combine(_projectDir.FullName, "function_app.py")).Should().Be(userEdit);
    }

    [Fact]
    public async Task InitializeAsync_ForceOverwrites()
    {
        await RunAsync(force: false);
        File.WriteAllText(Path.Combine(_projectDir.FullName, "function_app.py"), "stale");

        await RunAsync(force: true);

        File.ReadAllText(Path.Combine(_projectDir.FullName, "function_app.py")).Should().Contain("FunctionApp");
    }

    [Fact]
    public async Task InitializeAsync_NoBundle_WritesMinimalHostJsonWithoutExtensionBundle()
    {
        await RunAsync(force: false, args: ["--no-bundles"]);

        string hostJsonPath = Path.Combine(_projectDir.FullName, "host.json");
        File.Exists(hostJsonPath).Should().BeTrue("host.json should be created even with --no-bundles");
        string content = File.ReadAllText(hostJsonPath);
        content.Should().Contain("\"version\"");
        content.Should().NotContain("extensionBundle");
    }

    [Fact]
    public async Task InitializeAsync_BundlesChannel_Preview_WritesPreviewBundleId()
    {
        await RunAsync(force: false, args: ["--bundles-channel", "Preview"]);

        string hostJsonPath = Path.Combine(_projectDir.FullName, "host.json");
        var root = JsonNode.Parse(File.ReadAllText(hostJsonPath));
        root!["extensionBundle"]!["id"]!.GetValue<string>().Should().Be("Microsoft.Azure.Functions.ExtensionBundle.Preview");
    }

    [Fact]
    public async Task InitializeAsync_BundlesChannel_Experimental_WritesExperimentalBundleId()
    {
        await RunAsync(force: false, args: ["--bundles-channel", "Experimental"]);

        string hostJsonPath = Path.Combine(_projectDir.FullName, "host.json");
        var root = JsonNode.Parse(File.ReadAllText(hostJsonPath));
        root!["extensionBundle"]!["id"]!.GetValue<string>().Should().Be("Microsoft.Azure.Functions.ExtensionBundle.Experimental");
    }

    private Task RunAsync(bool force, string[]? args = null, string? language = null)
    {
        PythonProjectInitializer initializer = new();
        InitContext context = new(
            WorkingDirectory.FromExplicit(_projectDir.FullName),
            ProjectName: "test",
            Language: language,
            Force: force);

        RootCommand root = [];
        initializer.GetInitOptions(new InitOptionRegistry(root));

        ParseResult parseResult = root.Parse(args ?? []);
        return initializer.InitializeAsync(context, parseResult);
    }
}
