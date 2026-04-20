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

    [Theory]
    [InlineData("version")]
    [InlineData("help")]
    public void Parse_ValidCommand_DoesNotProduceErrors(string commandName)
    {
        var root = Parser.CreateCommand(_interaction);
        var result = root.Parse(commandName);

        Assert.Empty(result.Errors);
    }
}
