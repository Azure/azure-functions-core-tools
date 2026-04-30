// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
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
}
