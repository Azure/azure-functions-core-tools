// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class InitCommandTests
{
    private readonly TestInteractionService _interaction;
    private readonly RecordingWorkloadHintRenderer _hintRenderer;
    private readonly ILocalSettingsProvider _localSettings;

    public InitCommandTests()
    {
        _interaction = new TestInteractionService();
        _hintRenderer = new RecordingWorkloadHintRenderer();
        _localSettings = Substitute.For<ILocalSettingsProvider>();
        _localSettings.Get(Arg.Any<DirectoryInfo>()).Returns(LocalSettingsSnapshot.Empty);
    }

    private InitCommand CreateCommand(IEnumerable<IProjectInitializer> initializers) =>
        new(_interaction, _hintRenderer, _localSettings, initializers);

    [Fact]
    public void InitCommand_HasExpectedOptions()
    {
        var cmd = CreateCommand([]);
        var optionNames = cmd.Options.Select(o => o.Name).ToList();

        Assert.Contains("--stack", optionNames);
        Assert.Contains("--name", optionNames);
        Assert.Contains("--language", optionNames);
        Assert.Contains("--force", optionNames);
    }

    [Fact]
    public void InitCommand_StackOptionDescription_NoInitializers_PointsAtWorkloadInstall()
    {
        var cmd = CreateCommand([]);
        string description = cmd.StackOption.Description ?? string.Empty;

        Assert.Contains("Install a stack workload", description);
        Assert.Contains("func workload install", description);
    }

    [Fact]
    public void InitCommand_StackOptionDescription_ListsInstalledStacks_SortedAndLowercased()
    {
        var cmd = CreateCommand(
            [new FakeProjectInitializer("Python"), new FakeProjectInitializer("dotnet"), new FakeProjectInitializer("node")]);
        string description = cmd.StackOption.Description ?? string.Empty;

        Assert.Contains("Supported values: dotnet, node, python.", description);
    }

    [Fact]
    public void InitCommand_HelpFooterHint_PointsAtWorkloadSearch()
    {
        var cmd = CreateCommand([]);

        Assert.Contains("func workload search --stack", cmd.GetHelpFooterHint() ?? string.Empty);
    }

    [Fact]
    public void InitCommand_RegisteredInParser()
    {
        var root = TestParser.CreateRoot(_interaction);
        var names = root.Subcommands.Select(c => c.Name).ToList();

        Assert.Contains("init", names);
    }

    [Fact]
    public void InitCommand_HasPathArgument()
    {
        var cmd = CreateCommand([]);
        Assert.Single(cmd.Arguments);
        Assert.Equal("path", cmd.Arguments[0].Name);
    }

    [Fact]
    public async Task InitCommand_CreatesMissingDirectoryBeforeDispatch()
    {
        // Even when --stack is missing, InitCommand creates the target
        // directory before validating (directory creation is unconditional).
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        Assert.False(Directory.Exists(newDir));

        try
        {
            var root = TestParser.CreateRoot(_interaction);
            var result = root.Parse($"init \"{newDir}\"");

            var exitCode = await result.InvokeAsync();

            // No --stack provided → exit 1, but directory was still created.
            Assert.Equal(1, exitCode);
            Assert.True(Directory.Exists(newDir));
        }
        finally
        {
            if (Directory.Exists(newDir))
            {
                Directory.Delete(newDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InitCommand_ScaffoldsCliConfigurationFile_OnSuccess()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");

        try
        {
            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: "python", stack: "python");

            Assert.Equal(0, exitCode);
            Assert.True(initializer.WasInvoked);

            string configPath = Path.Combine(newDir, ".func", "config.json");
            Assert.True(File.Exists(configPath), $"Expected .func/config.json at {configPath}.");

            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            JsonElement stack = doc.RootElement.GetProperty("stack");
            Assert.Equal("python", stack.GetProperty("runtime").GetString());
            Assert.Equal("python", stack.GetProperty("language").GetString());
        }
        finally
        {
            if (Directory.Exists(newDir))
            {
                Directory.Delete(newDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InitCommand_DoesNotOverwriteExistingCliConfigurationFile()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(Path.Combine(newDir, ".func"));
            string configPath = Path.Combine(newDir, ".func", "config.json");
            const string existingContent = """{"stack":"node","language":"typescript"}""";
            File.WriteAllText(configPath, existingContent);

            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: "python", stack: "python");

            // Folder is already a Functions project (.func/config.json present);
            // command refuses without --force, initializer never runs.
            Assert.Equal(1, exitCode);
            Assert.False(initializer.WasInvoked);
            Assert.Equal(existingContent, File.ReadAllText(configPath));
        }
        finally
        {
            if (Directory.Exists(newDir))
            {
                Directory.Delete(newDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task InitCommand_OmitsLanguageWhenNotSpecified()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");

        try
        {
            var initializer = new FakeProjectInitializer("dotnet");
            int exitCode = await RunInitAsync(newDir, initializer, language: null, stack: "dotnet");

            Assert.Equal(0, exitCode);

            string configPath = Path.Combine(newDir, ".func", "config.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            JsonElement stack = doc.RootElement.GetProperty("stack");
            Assert.Equal("dotnet", stack.GetProperty("runtime").GetString());
            Assert.False(stack.TryGetProperty("language", out _));
        }
        finally
        {
            if (Directory.Exists(newDir))
            {
                Directory.Delete(newDir, recursive: true);
            }
        }
    }

    private async Task<int> RunInitAsync(
        string path,
        IProjectInitializer initializer,
        string? language,
        string? stack = null,
        bool force = false)
    {
        return await RunInitAsync(path, [initializer], language, stack, force);
    }

    private async Task<int> RunInitAsync(
        string path,
        IReadOnlyList<IProjectInitializer> initializers,
        string? language = null,
        string? stack = null,
        bool force = false,
        IReadOnlyList<string>? extraArgs = null)
    {
        var cmd = CreateCommand(initializers);
        var root = new RootCommand { cmd };
        var args = new List<string> { "init", path };
        if (stack is not null)
        {
            args.Add("--stack");
            args.Add(stack);
        }
        if (language is not null)
        {
            args.Add("--language");
            args.Add(language);
        }
        if (force)
        {
            args.Add("--force");
        }
        if (extraArgs is not null)
        {
            args.AddRange(extraArgs);
        }
        ParseResult result = root.Parse(args.ToArray());
        return await result.InvokeAsync();
    }

    [Fact]
    public async Task InitCommand_NoWorkloadsInstalled_ExitsWithHint()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            int exitCode = await RunInitAsync(newDir, initializers: []);

            Assert.Equal(1, exitCode);

            WorkloadHint hint = Assert.Single(_hintRenderer.Hints);
            Assert.Equal(WorkloadHintKind.NoWorkloadsInstalled, hint.Kind);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_StackProvidedButNoMatch_ExitsWithHint()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: null, stack: "ruby");

            Assert.Equal(1, exitCode);
            Assert.False(initializer.WasInvoked);

            WorkloadHint hint = Assert.Single(_hintRenderer.Hints);
            Assert.Equal(WorkloadHintKind.NoMatchingStack, hint.Kind);
            Assert.Equal("ruby", hint.RequestedStack);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_MultipleWorkloads_NoStack_NonInteractive_ExitsWithHint()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            var python = new FakeProjectInitializer("python");
            var node = new FakeProjectInitializer("node");
            int exitCode = await RunInitAsync(newDir, [python, node]);

            Assert.Equal(1, exitCode);
            Assert.False(python.WasInvoked);
            Assert.False(node.WasInvoked);

            WorkloadHint hint = Assert.Single(_hintRenderer.Hints);
            Assert.Equal(WorkloadHintKind.AmbiguousStackChoice, hint.Kind);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_SingleWorkload_NoStack_AutoSelectsAndRuns()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: null);

            Assert.Equal(0, exitCode);
            Assert.True(initializer.WasInvoked);
            AssertConfigJsonHasShape(newDir, expectedStack: "python", expectedLanguage: null);

            WorkloadHint hint = Assert.Single(_hintRenderer.Hints);
            Assert.Equal(WorkloadHintKind.AutoSelectedSoleWorkload, hint.Kind);
            Assert.Equal("python", hint.RequestedStack);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_StackProvided_WritesConfigAndInvokesInitializer()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: "python", stack: "python");

            Assert.Equal(0, exitCode);
            Assert.True(initializer.WasInvoked);
            AssertConfigJsonHasShape(newDir, expectedStack: "python", expectedLanguage: "python");

            // No hint rendered when --stack matched directly.
            Assert.Empty(_hintRenderer.Hints);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_AdoptsExistingProject_WhenOnlyHostJsonPresent()
    {
        // host.json without .func/config.json means a pre-v5 project. `func init`
        // should adopt it: write .func/config.json, preserve host.json and any
        // user source, and skip scaffolding.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            string hostPath = Path.Combine(newDir, "host.json");
            const string existingContent = "{\"version\":\"2.0\",\"customMarker\":true}";
            File.WriteAllText(hostPath, existingContent);
            string userFile = Path.Combine(newDir, "MyFunction.cs");
            File.WriteAllText(userFile, "// user code");

            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: null, stack: "python");

            Assert.Equal(0, exitCode);
            // Scaffolding is skipped: existing files survive untouched and the
            // initializer is not invoked.
            Assert.False(initializer.WasInvoked);
            Assert.Equal(existingContent, File.ReadAllText(hostPath));
            Assert.True(File.Exists(userFile));
            AssertConfigJsonHasShape(newDir, expectedStack: "python", expectedLanguage: null);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_RefusesEarly_WhenFullyInitialized()
    {
        // A directory that already has .func/config.json (a v5 project) is
        // refused without --force: no overwrite, no scaffolding, exit 1.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(newDir, ".func"));
            File.WriteAllText(Path.Combine(newDir, ".func", "config.json"), "{}");

            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: null, stack: "python");

            Assert.Equal(1, exitCode);
            Assert.False(initializer.WasInvoked);
            Assert.Equal("{}", File.ReadAllText(Path.Combine(newDir, ".func", "config.json")));
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_OverwritesExistingProject_WhenForceIsSet()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            string hostPath = Path.Combine(newDir, "host.json");
            File.WriteAllText(hostPath, "{\"version\":\"2.0\",\"customMarker\":true}");

            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: null, stack: "python", force: true);

            Assert.Equal(0, exitCode);
            Assert.True(initializer.WasInvoked);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Force_ClearsDirectoryButPreservesDotGit()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            // Pre-existing junk from a prior stack.
            File.WriteAllText(Path.Combine(newDir, "package.json"), "{}");
            File.WriteAllText(Path.Combine(newDir, "requirements.txt"), "azure-functions");
            Directory.CreateDirectory(Path.Combine(newDir, "node_modules"));
            File.WriteAllText(Path.Combine(newDir, "node_modules", "marker"), "x");

            // .git must survive.
            Directory.CreateDirectory(Path.Combine(newDir, ".git"));
            File.WriteAllText(Path.Combine(newDir, ".git", "HEAD"), "ref: refs/heads/main");

            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: null, stack: "python", force: true);

            Assert.Equal(0, exitCode);
            Assert.False(File.Exists(Path.Combine(newDir, "package.json")));
            Assert.False(File.Exists(Path.Combine(newDir, "requirements.txt")));
            Assert.False(Directory.Exists(Path.Combine(newDir, "node_modules")));
            Assert.True(File.Exists(Path.Combine(newDir, ".git", "HEAD")));
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Force_WritesWarningBeforeClearing()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "leftover.txt"), "x");

            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: null, stack: "python", force: true);

            Assert.Equal(0, exitCode);
            Assert.Contains(_interaction.Lines, l => l.StartsWith("WARNING:") && l.Contains("--force will delete"));
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_SnapsViaRuntimeAlias()
    {
        // local.settings.json declares "dotnet-isolated" but the dotnet
        // initializer owns that runtime as an alias. We should snap to the
        // initializer's canonical stack id ("dotnet"), not error.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");
            StubLocalSettingsRuntime("dotnet-isolated");

            var dotnet = new FakeProjectInitializer("dotnet", workerRuntimeAliases: ["dotnet-isolated"]);
            int exitCode = await RunInitAsync(newDir, [dotnet], language: null, stack: null);

            Assert.Equal(0, exitCode);
            Assert.False(dotnet.WasInvoked);
            AssertConfigJsonHasShape(newDir, expectedStack: "dotnet", expectedLanguage: null);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_StackOptionAgreesViaRuntimeAlias()
    {
        // --stack dotnet against a project whose local.settings.json says
        // "dotnet-isolated" should resolve via the alias and adopt
        // (not flag a phantom conflict).
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");
            StubLocalSettingsRuntime("dotnet-isolated");

            var dotnet = new FakeProjectInitializer("dotnet", workerRuntimeAliases: ["dotnet-isolated"]);
            int exitCode = await RunInitAsync(newDir, dotnet, language: null, stack: "dotnet");

            Assert.Equal(0, exitCode);
            Assert.False(dotnet.WasInvoked);
            AssertConfigJsonHasShape(newDir, expectedStack: "dotnet", expectedLanguage: null);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_StackOptionAlias_Recognized()
    {
        // The reverse direction: --stack dotnet-isolated (an alias) should
        // resolve to the dotnet initializer when adopting an empty
        // local.settings.json project.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");

            var interactive = new InteractiveTestInteractionService();
            var dotnet = new FakeProjectInitializer("dotnet", workerRuntimeAliases: ["dotnet-isolated"]);
            var command = new InitCommand(interactive, _hintRenderer, _localSettings, [dotnet]);
            var root = new RootCommand { command };

            int exitCode = await root.Parse(["init", newDir, "--stack", "dotnet-isolated"]).InvokeAsync();

            Assert.Equal(0, exitCode);
            Assert.False(dotnet.WasInvoked);
            AssertConfigJsonHasShape(newDir, expectedStack: "dotnet", expectedLanguage: null);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_SnapsStackFromLocalSettings()
    {
        // host.json + local.settings.json FUNCTIONS_WORKER_RUNTIME=python → snap
        // to python, initializer is not invoked (existing project), config
        // records python.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");
            StubLocalSettingsRuntime("python");

            var python = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, [python], language: null, stack: null);

            Assert.Equal(0, exitCode);
            Assert.False(python.WasInvoked);
            AssertConfigJsonHasShape(newDir, expectedStack: "python", expectedLanguage: null);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_UninstalledStack_RefusesAndSuggestsSetup()
    {
        // project_runtime points at a stack with no installed initializer:
        // refuse rather than write a config we can't validate. The setup
        // hint uses a `<stack>` placeholder (not the raw runtime value),
        // because the runtime string may itself not be a valid feature id.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");
            StubLocalSettingsRuntime("ruby");

            var python = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, [python], language: null, stack: null);

            Assert.Equal(1, exitCode);
            Assert.False(python.WasInvoked);
            Assert.False(File.Exists(Path.Combine(newDir, ".func", "config.json")));
            Assert.Contains(_interaction.Lines, l =>
                l.StartsWith("ERROR:")
                && l.Contains("'ruby' stack is not installed")
                && l.Contains("func setup --features <stack>"));
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_RefusesWhenStackOptionConflictsWithProjectRuntime()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");
            StubLocalSettingsRuntime("python");

            var python = new FakeProjectInitializer("python");
            var node = new FakeProjectInitializer("node");
            int exitCode = await RunInitAsync(newDir, [python, node], language: null, stack: "node");

            Assert.Equal(1, exitCode);
            Assert.False(python.WasInvoked);
            Assert.False(node.WasInvoked);
            Assert.False(File.Exists(Path.Combine(newDir, ".func", "config.json")));
            Assert.Contains(_interaction.Lines, l => l.Contains("conflicts") && l.Contains("python") && l.Contains("node"));
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_AllowsStackOptionWhenItMatchesProjectRuntime()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");
            StubLocalSettingsRuntime("python");

            var python = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, python, language: null, stack: "python");

            Assert.Equal(0, exitCode);
            Assert.False(python.WasInvoked);
            AssertConfigJsonHasShape(newDir, expectedStack: "python", expectedLanguage: null);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_NoProjectSignal_NonInteractive_Refuses()
    {
        // In adopt mode with no --stack and no FUNCTIONS_WORKER_RUNTIME, we
        // don't auto-select even when only one initializer is installed:
        // writing the wrong stack into an existing project's config is worse
        // than failing fast. Non-interactive callers (CI, piped) get an
        // actionable error.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");

            var python = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, python, language: null);

            Assert.Equal(1, exitCode);
            Assert.False(python.WasInvoked);
            Assert.False(File.Exists(Path.Combine(newDir, ".func", "config.json")));
            Assert.Contains(_interaction.Lines, l =>
                l.StartsWith("ERROR:") && l.Contains("Couldn't detect the project's stack"));
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_NoProjectSignal_Interactive_PromptsForStack()
    {
        // Interactive callers get a prompt that names adoption explicitly so
        // they know what they're answering for. The prompt's first choice is
        // selected by the test interaction service.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");

            var interactive = new InteractiveTestInteractionService();
            var python = new FakeProjectInitializer("python");
            var command = new InitCommand(interactive, _hintRenderer, _localSettings, [python]);
            var root = new RootCommand { command };

            int exitCode = await root.Parse(["init", newDir]).InvokeAsync();

            Assert.Equal(0, exitCode);
            Assert.False(python.WasInvoked);
            AssertConfigJsonHasShape(newDir, expectedStack: "python", expectedLanguage: null);
            Assert.Contains(interactive.Lines, l =>
                l.StartsWith("SELECT:") && l.Contains("Adopting an existing project"));
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_StackOption_UninstalledStack_Refuses()
    {
        // --stack X where X isn't installed should be refused just like the
        // uninstalled local.settings.json case. Same hint, same exit code,
        // no config written.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");

            var python = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, [python], language: null, stack: "ruby");

            Assert.Equal(1, exitCode);
            Assert.False(python.WasInvoked);
            Assert.False(File.Exists(Path.Combine(newDir, ".func", "config.json")));
            Assert.Contains(_interaction.Lines, l =>
                l.StartsWith("ERROR:")
                && l.Contains("'ruby' stack is not installed")
                && l.Contains("func setup --features <stack>"));
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_Adopt_ForceOverridesConflict()
    {
        // --force is the documented escape hatch: it wipes the directory and
        // re-scaffolds with the requested stack, regardless of project signal.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{}");
            StubLocalSettingsRuntime("python");

            var node = new FakeProjectInitializer("node");
            int exitCode = await RunInitAsync(newDir, node, language: null, stack: "node", force: true);

            Assert.Equal(0, exitCode);
            Assert.True(node.WasInvoked);
            AssertConfigJsonHasShape(newDir, expectedStack: "node", expectedLanguage: null);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    private void StubLocalSettingsRuntime(string runtime)
    {
        var snapshot = new LocalSettingsSnapshot
        {
            Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["FUNCTIONS_WORKER_RUNTIME"] = runtime,
            },
        };
        _localSettings.Get(Arg.Any<DirectoryInfo>()).Returns(snapshot);
    }

    private static void AssertConfigJsonHasShape(string directory, string? expectedStack, string? expectedLanguage)
    {
        string configPath = Path.Combine(directory, ".func", "config.json");
        Assert.True(File.Exists(configPath), $"Expected .func/config.json at {configPath}.");

        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        if (expectedStack is null)
        {
            Assert.False(doc.RootElement.TryGetProperty("stack", out _));
        }
        else
        {
            Assert.Equal(expectedStack, doc.RootElement.GetProperty("stack").GetProperty("runtime").GetString());
        }
        if (expectedLanguage is null)
        {
            if (doc.RootElement.TryGetProperty("stack", out JsonElement stack))
            {
                Assert.False(stack.TryGetProperty("language", out _));
            }
        }
        else
        {
            Assert.Equal(expectedLanguage, doc.RootElement.GetProperty("stack").GetProperty("language").GetString());
        }
    }

    private static void CleanupDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task InitCommand_RejectsLanguageNotInSupportedList()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            var initializer = new FakeProjectInitializer("node", ["JavaScript", "TypeScript"]);
            int exitCode = await RunInitAsync(newDir, initializer, language: "python", stack: "node");

            Assert.Equal(1, exitCode);
            Assert.False(initializer.WasInvoked);
            Assert.Contains(_interaction.Lines, l => l.Contains("not supported", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_OmitsLanguageWhenStackHasOnlySupportedLanguage()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            var initializer = new FakeProjectInitializer("python", ["Python"]);
            int exitCode = await RunInitAsync(newDir, initializer, language: null, stack: "python");

            Assert.Equal(0, exitCode);

            // With a single supported language, the stack runtime implies the
            // language; the config skips the redundant 'language' field.
            AssertConfigJsonHasShape(newDir, expectedStack: "python", expectedLanguage: null);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_ErrorsWhenMultipleLanguagesAndNonInteractive()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            var initializer = new FakeProjectInitializer("node", ["JavaScript", "TypeScript"]);
            int exitCode = await RunInitAsync(newDir, initializer, language: null, stack: "node");

            Assert.Equal(1, exitCode);
            Assert.False(initializer.WasInvoked);
            Assert.Contains(_interaction.Lines, l => l.Contains("--language", StringComparison.Ordinal));
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public void InitCommand_DedupesContributedOptionsByName()
    {
        // Two workloads contributing the same option name (e.g. --no-bundles) must
        // appear once in --help and resolve to a single canonical Option instance.
        var first = new FakeProjectInitializer(
            "node",
            optionFactory: r => [r.GetOrAdd(new Option<bool>("--no-bundles"))]);
        var second = new FakeProjectInitializer(
            "python",
            optionFactory: r => [r.GetOrAdd(new Option<bool>("--no-bundles"))]);

        var cmd = CreateCommand([first, second]);

        int matches = cmd.Options.Count(o => o.Name == "--no-bundles");
        Assert.Equal(1, matches);
        Assert.Same(first.ContributedOptions[0], second.ContributedOptions[0]);
    }

    [Fact]
    public async Task InitCommand_SharedOption_IsReadCorrectlyByLaterWorkload()
    {
        // When `python` is selected but `node` was registered first, python must still see
        // the user-supplied value for the shared `--no-bundles` option.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            bool? observed = null;
            var node = new FakeProjectInitializer(
                "node",
                optionFactory: r => [r.GetOrAdd(new Option<bool>("--no-bundles"))]);
            var python = new FakeProjectInitializer(
                "python",
                optionFactory: r => [r.GetOrAdd(new Option<bool>("--no-bundles"))],
                onInitialize: (init, parseResult) =>
                {
                    var opt = (Option<bool>)init.ContributedOptions[0];
                    observed = parseResult.GetValue(opt);
                });

            int exitCode = await RunInitAsync(
                newDir,
                [node, python],
                language: null,
                stack: "python",
                extraArgs: ["--no-bundles"]);

            Assert.Equal(0, exitCode);
            Assert.True(observed);
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public void InitCommand_ThrowsWhenWorkloadsContributeSameNameDifferentType()
    {
        var first = new FakeProjectInitializer(
            "node",
            optionFactory: r => [r.GetOrAdd(new Option<bool>("--no-bundles"))]);
        var second = new FakeProjectInitializer(
            "python",
            optionFactory: r => [r.GetOrAdd(new Option<string>("--no-bundles"))]);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CreateCommand([first, second]));

        Assert.Contains("python", ex.Message);
        Assert.Contains("node", ex.Message);
        Assert.Contains("--no-bundles", ex.Message);
    }

    [Fact]
    public void InitCommand_ThrowsWhenAliasCollidesAcrossWorkloads()
    {
        // Two workloads both want -c as an alias, but for different options.
        var first = new FakeProjectInitializer(
            "node",
            optionFactory: r => [r.GetOrAdd(new Option<string>("--bundles-channel", "-c"))]);
        var second = new FakeProjectInitializer(
            "go",
            optionFactory: r => [r.GetOrAdd(new Option<string>("--clean", "-c"))]);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CreateCommand([first, second]));

        Assert.Contains("-c", ex.Message);
        Assert.Contains("--clean", ex.Message);
        Assert.Contains("--bundles-channel", ex.Message);
    }

    private sealed class FakeProjectInitializer(
        string stack,
        IReadOnlyList<string>? supportedLanguages = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? aliases = null,
        Func<IInitOptionRegistry, IReadOnlyList<Option>>? optionFactory = null,
        Action<FakeProjectInitializer, ParseResult>? onInitialize = null,
        IReadOnlyList<string>? workerRuntimeAliases = null) : IProjectInitializer
    {
        public string Stack { get; } = stack;

        public IReadOnlyList<string> SupportedLanguages { get; } = supportedLanguages ?? [];

        public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; } =
            aliases ?? new Dictionary<string, IReadOnlyList<string>>();

        public IReadOnlyList<string> WorkerRuntimeAliases { get; } = workerRuntimeAliases ?? [];

        public bool WasInvoked { get; private set; }

        public IReadOnlyList<Option> ContributedOptions { get; private set; } = [];

        public IReadOnlyList<Option> GetInitOptions(IInitOptionRegistry registry)
        {
            ContributedOptions = optionFactory?.Invoke(registry) ?? [];
            return ContributedOptions;
        }

        public Task InitializeAsync(InitContext context, ParseResult parseResult, CancellationToken cancellationToken = default)
        {
            WasInvoked = true;
            onInitialize?.Invoke(this, parseResult);
            return Task.CompletedTask;
        }
    }

    private sealed class InteractiveTestInteractionService : TestInteractionService
    {
        public override bool IsInteractive => true;
    }
}
