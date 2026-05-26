// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class NewCommandTests
{
    private readonly TestInteractionService _interaction;
    private readonly RecordingWorkloadHintRenderer _hintRenderer;

    public NewCommandTests()
    {
        _interaction = new TestInteractionService();
        _hintRenderer = new RecordingWorkloadHintRenderer();
    }

    [Fact]
    public void NewCommand_HasExpectedOptions()
    {
        var cmd = new NewCommand(_hintRenderer);
        var optionNames = cmd.Options.Select(o => o.Name).ToList();

        Assert.Contains("--name", optionNames);
        Assert.Contains("--template", optionNames);
        Assert.Contains("--force", optionNames);
    }

    [Fact]
    public void NewCommand_RegisteredInParser()
    {
        var root = TestParser.CreateRoot(_interaction);
        var names = root.Subcommands.Select(c => c.Name).ToList();

        Assert.Contains("new", names);
    }

    [Fact]
    public void NewCommand_HasPathArgument()
    {
        var cmd = new NewCommand(_hintRenderer);
        Assert.Single(cmd.Arguments);
        Assert.Equal("path", cmd.Arguments[0].Name);
    }
}
