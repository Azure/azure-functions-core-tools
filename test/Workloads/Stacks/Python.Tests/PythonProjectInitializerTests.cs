// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;
using Xunit;

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
        Assert.Equal("python", new PythonProjectInitializer().Stack);
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
        Assert.Equal(expected, new PythonProjectInitializer().NormalizeLanguage(input));
    }

    [Fact]
    public async Task InitializeAsync_UnsupportedLanguage_Throws()
    {
        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => RunAsync(force: false, language: "ruby"));

        Assert.Contains("ruby", ex.Message);
    }

    [Fact]
    public async Task InitializeAsync_AliasPy_Succeeds()
    {
        await RunAsync(force: false, language: "py");

        Assert.True(File.Exists(Path.Combine(_projectDir.FullName, "function_app.py")));
    }

    [Fact]
    public void GetInitOptions_RegistersBundleOptions()
    {
        RootCommand root = [];
        IReadOnlyList<Option> options = new PythonProjectInitializer().GetInitOptions(new InitOptionRegistry(root));
        IReadOnlyList<string> names = [.. options.Select(o => o.Name)];

        Assert.Contains("--no-bundles", names);
        Assert.Contains("--bundles-channel", names);
    }

    [Fact]
    public async Task InitializeAsync_WritesExpectedFiles()
    {
        await RunAsync(force: false);

        Assert.True(File.Exists(Path.Combine(_projectDir.FullName, "function_app.py")));
        Assert.Contains("FunctionApp", File.ReadAllText(Path.Combine(_projectDir.FullName, "function_app.py")));

        Assert.True(File.Exists(Path.Combine(_projectDir.FullName, "requirements.txt")));
        Assert.Contains("azure-functions", File.ReadAllText(Path.Combine(_projectDir.FullName, "requirements.txt")));

        Assert.True(File.Exists(Path.Combine(_projectDir.FullName, "getting_started.md")));
        string gettingStarted = File.ReadAllText(Path.Combine(_projectDir.FullName, "getting_started.md"));
        Assert.Contains("https://aka.ms/pythonprogrammingmodel", gettingStarted);
        Assert.DoesNotContain("aka.ms/azure-functions/python/v2", gettingStarted);

        Assert.True(File.Exists(Path.Combine(_projectDir.FullName, ".gitignore")));

        Assert.True(File.Exists(Path.Combine(_projectDir.FullName, "local.settings.json")));
        var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "local.settings.json")));
        Assert.Equal("python", settings.RootElement.GetProperty("Values").GetProperty("FUNCTIONS_WORKER_RUNTIME").GetString());
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

        await RunAsync(force: false);

        JsonNode? bundle = JsonNode.Parse(File.ReadAllText(Path.Combine(_projectDir.FullName, "host.json")))!["extensionBundle"];
        Assert.Equal("custom", bundle!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent_WithoutForce()
    {
        // First run scaffolds; user-edited file must survive a second run.
        await RunAsync(force: false);
        const string userEdit = "import azure.functions as func\n# user edits\n";
        File.WriteAllText(Path.Combine(_projectDir.FullName, "function_app.py"), userEdit);

        await RunAsync(force: false);

        Assert.Equal(userEdit, File.ReadAllText(Path.Combine(_projectDir.FullName, "function_app.py")));
    }

    [Fact]
    public async Task InitializeAsync_ForceOverwrites()
    {
        await RunAsync(force: false);
        File.WriteAllText(Path.Combine(_projectDir.FullName, "function_app.py"), "stale");

        await RunAsync(force: true);

        Assert.Contains("FunctionApp", File.ReadAllText(Path.Combine(_projectDir.FullName, "function_app.py")));
    }

    [Fact]
    public async Task InitializeAsync_NoBundle_WritesMinimalHostJsonWithoutExtensionBundle()
    {
        await RunAsync(force: false, args: ["--no-bundles"]);

        string hostJsonPath = Path.Combine(_projectDir.FullName, "host.json");
        Assert.True(File.Exists(hostJsonPath), "host.json should be created even with --no-bundles");
        string content = File.ReadAllText(hostJsonPath);
        Assert.Contains("\"version\"", content);
        Assert.DoesNotContain("extensionBundle", content);
    }

    [Fact]
    public async Task InitializeAsync_BundlesChannel_Preview_WritesPreviewBundleId()
    {
        await RunAsync(force: false, args: ["--bundles-channel", "Preview"]);

        string hostJsonPath = Path.Combine(_projectDir.FullName, "host.json");
        var root = JsonNode.Parse(File.ReadAllText(hostJsonPath));
        Assert.Equal(
            "Microsoft.Azure.Functions.ExtensionBundle.Preview",
            root!["extensionBundle"]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task InitializeAsync_BundlesChannel_Experimental_WritesExperimentalBundleId()
    {
        await RunAsync(force: false, args: ["--bundles-channel", "Experimental"]);

        string hostJsonPath = Path.Combine(_projectDir.FullName, "host.json");
        var root = JsonNode.Parse(File.ReadAllText(hostJsonPath));
        Assert.Equal(
            "Microsoft.Azure.Functions.ExtensionBundle.Experimental",
            root!["extensionBundle"]!["id"]!.GetValue<string>());
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
