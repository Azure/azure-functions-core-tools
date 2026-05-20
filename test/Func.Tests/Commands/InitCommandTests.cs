// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class InitCommandTests
{
    private readonly TestInteractionService _interaction;
    private readonly RecordingWorkloadHintRenderer _hintRenderer;

    public InitCommandTests()
    {
        _interaction = new TestInteractionService();
        _hintRenderer = new RecordingWorkloadHintRenderer();
    }

    [Fact]
    public void InitCommand_HasExpectedOptions()
    {
        var cmd = new InitCommand(_interaction, _hintRenderer, []);
        var optionNames = cmd.Options.Select(o => o.Name).ToList();

        Assert.Contains("--stack", optionNames);
        Assert.Contains("--name", optionNames);
        Assert.Contains("--language", optionNames);
        Assert.Contains("--force", optionNames);
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
        var cmd = new InitCommand(_interaction, _hintRenderer, []);
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
            JsonElement stack = doc.RootElement.GetProperty("Stack");
            Assert.Equal("python", stack.GetProperty("Runtime").GetString());
            Assert.Equal("python", stack.GetProperty("Language").GetString());
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
            JsonElement stack = doc.RootElement.GetProperty("Stack");
            Assert.Equal("dotnet", stack.GetProperty("Runtime").GetString());
            Assert.False(stack.TryGetProperty("Language", out _));
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
        bool force = false)
    {
        var cmd = new InitCommand(_interaction, _hintRenderer, initializers);
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
    public async Task InitCommand_PreservesExistingHostJson_WhenForceIsNotSet()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            string hostPath = Path.Combine(newDir, "host.json");
            const string existingContent = "{\"version\":\"2.0\",\"customMarker\":true}";
            File.WriteAllText(hostPath, existingContent);

            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: null, stack: "python");

            // host.json present means the folder is already a Functions project;
            // command refuses without --force, initializer never runs.
            Assert.Equal(1, exitCode);
            Assert.False(initializer.WasInvoked);
            Assert.Equal(existingContent, File.ReadAllText(hostPath));
        }
        finally
        {
            CleanupDirectory(newDir);
        }
    }

    [Fact]
    public async Task InitCommand_RefusesEarly_WhenAlreadyInitialized()
    {
        // A directory that already has a host.json is treated as initialized:
        // no config write, exit 1.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(newDir);
            File.WriteAllText(Path.Combine(newDir, "host.json"), "{\"version\":\"2.0\"}");

            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: null, stack: "python");

            Assert.Equal(1, exitCode);
            Assert.False(initializer.WasInvoked);
            Assert.False(File.Exists(Path.Combine(newDir, ".func", "config.json")));
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

    private static void AssertConfigJsonHasShape(string directory, string? expectedStack, string? expectedLanguage)
    {
        string configPath = Path.Combine(directory, ".func", "config.json");
        Assert.True(File.Exists(configPath), $"Expected .func/config.json at {configPath}.");

        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        if (expectedStack is null)
        {
            Assert.False(doc.RootElement.TryGetProperty("Stack", out _));
        }
        else
        {
            Assert.Equal(expectedStack, doc.RootElement.GetProperty("Stack").GetProperty("Runtime").GetString());
        }
        if (expectedLanguage is null)
        {
            if (doc.RootElement.TryGetProperty("Stack", out JsonElement stack))
            {
                Assert.False(stack.TryGetProperty("Language", out _));
            }
        }
        else
        {
            Assert.Equal(expectedLanguage, doc.RootElement.GetProperty("Stack").GetProperty("Language").GetString());
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
    public async Task InitCommand_AutoSelectsLanguageWhenInitializerHasOnlyOne()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        try
        {
            var initializer = new FakeProjectInitializer("python", ["Python"]);
            int exitCode = await RunInitAsync(newDir, initializer, language: null, stack: "python");

            Assert.Equal(0, exitCode);
            AssertConfigJsonHasShape(newDir, expectedStack: "python", expectedLanguage: "python");
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

    private sealed class FakeProjectInitializer(string stack, IReadOnlyList<string>? supportedLanguages = null) : IProjectInitializer
    {
        public string Stack { get; } = stack;

        public IReadOnlyList<string> SupportedLanguages { get; } = supportedLanguages ?? [];

        public bool WasInvoked { get; private set; }

        public IReadOnlyList<Option> GetInitOptions() => [];

        public Task InitializeAsync(InitContext context, ParseResult parseResult, CancellationToken cancellationToken = default)
        {
            WasInvoked = true;
            return Task.CompletedTask;
        }
    }
}
