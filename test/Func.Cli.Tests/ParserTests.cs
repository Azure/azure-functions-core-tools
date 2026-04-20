// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Console;
using Xunit;

namespace Azure.Functions.Cli.Tests;

public class ParserTests
{
    private readonly TestInteractionService _interaction;

    public ParserTests()
    {
        _interaction = new TestInteractionService();
    }

    [Fact]
    public void CreateCommand_ReturnsRootCommand()
    {
        var root = Parser.CreateCommand(_interaction);

        Assert.NotNull(root);
        Assert.IsType<FuncRootCommand>(root);
    }

    [Fact]
    public void CreateCommand_HasExpectedSubcommands()
    {
        var root = Parser.CreateCommand(_interaction);
        var names = root.Subcommands.Select(c => c.Name).ToList();

        // Visible commands
        Assert.Contains("init", names);
        Assert.Contains("new", names);
        Assert.Contains("start", names);
        // help and version are hidden (accessible via --help/--version)
        Assert.Contains("version", names);
        Assert.Contains("help", names);
    }



    [Fact]
    public void CreateCommand_HasGlobalOptions()
    {
        var root = Parser.CreateCommand(_interaction);
        var optionNames = root.Options.Select(o => o.Name).ToList();

        Assert.Contains("--verbose", optionNames);
    }

    [Fact]
    public void Parse_StartWithPath_DoesNotProduceErrors()
    {
        var root = Parser.CreateCommand(_interaction);
        var result = root.Parse("start /tmp");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_InitWithPath_DoesNotProduceErrors()
    {
        var root = Parser.CreateCommand(_interaction);
        var result = root.Parse("init /tmp/myproject");

        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("help")]
    [InlineData("init")]
    [InlineData("new")]
    [InlineData("start")]
    public void Parse_ValidCommand_DoesNotProduceErrors(string commandName)
    {
        var root = Parser.CreateCommand(_interaction);
        var result = root.Parse(commandName);

        Assert.Empty(result.Errors);
    }



    [Fact]
    public void Commands_AreBaseCommandInstances()
    {
        var root = Parser.CreateCommand(_interaction);
        var visibleCommands = root.Subcommands.Where(c => !c.Hidden).ToList();

        foreach (var cmd in visibleCommands)
        {
            Assert.True(cmd is BaseCommand, $"Command '{cmd.Name}' should inherit BaseCommand");
        }
    }
}
