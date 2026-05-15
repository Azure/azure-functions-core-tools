// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using Azure.Functions.Cli.Commands;
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
        // No workloads installed → InitCommand short-circuits to a hint, but it
        // must still have created the [path] directory by then. This guards the
        // post-ApplyPath refactor: previously `ApplyPath(parseResult, createIfNotExists: true)`
        // did this; now `WorkingDirectory.CreateIfNotExists()` does.
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");
        Assert.False(Directory.Exists(newDir));

        try
        {
            var root = TestParser.CreateRoot(_interaction);
            var result = root.Parse($"init \"{newDir}\"");

            var exitCode = await result.InvokeAsync();

            Assert.Equal(1, exitCode); // no workloads installed
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
    public async Task InitCommand_ScaffoldsFuncProjectConfig_OnSuccess()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");

        try
        {
            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: "python");

            Assert.Equal(0, exitCode);
            Assert.True(initializer.WasInvoked);

            string configPath = Path.Combine(newDir, ".func", "config.json");
            Assert.True(File.Exists(configPath), $"Expected .func/config.json at {configPath}.");

            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            Assert.Equal("python", doc.RootElement.GetProperty("stack").GetString());
            Assert.Equal("python", doc.RootElement.GetProperty("language").GetString());
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
    public async Task InitCommand_DoesNotOverwriteExistingFuncProjectConfig()
    {
        var newDir = Path.Combine(Path.GetTempPath(), $"func-init-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(Path.Combine(newDir, ".func"));
            string configPath = Path.Combine(newDir, ".func", "config.json");
            const string existingContent = """{"stack":"node","language":"typescript"}""";
            File.WriteAllText(configPath, existingContent);

            var initializer = new FakeProjectInitializer("python");
            int exitCode = await RunInitAsync(newDir, initializer, language: "python");

            Assert.Equal(0, exitCode);
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
            int exitCode = await RunInitAsync(newDir, initializer, language: null);

            Assert.Equal(0, exitCode);

            string configPath = Path.Combine(newDir, ".func", "config.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            Assert.Equal("dotnet", doc.RootElement.GetProperty("stack").GetString());
            Assert.False(doc.RootElement.TryGetProperty("language", out _));
        }
        finally
        {
            if (Directory.Exists(newDir))
            {
                Directory.Delete(newDir, recursive: true);
            }
        }
    }

    private async Task<int> RunInitAsync(string path, IProjectInitializer initializer, string? language)
    {
        var cmd = new InitCommand(_interaction, _hintRenderer, [initializer]);
        var root = new RootCommand { cmd };
        string args = language is null
            ? $"init \"{path}\""
            : $"init \"{path}\" --language {language}";
        ParseResult result = root.Parse(args);
        return await result.InvokeAsync();
    }

    private sealed class FakeProjectInitializer(string stack) : IProjectInitializer
    {
        public string Stack { get; } = stack;

        public IReadOnlyList<string> SupportedLanguages { get; } = [];

        public bool WasInvoked { get; private set; }

        public IReadOnlyList<Option> GetInitOptions() => [];

        public Task InitializeAsync(InitContext context, ParseResult parseResult, CancellationToken cancellationToken = default)
        {
            WasInvoked = true;
            return Task.CompletedTask;
        }
    }
}
