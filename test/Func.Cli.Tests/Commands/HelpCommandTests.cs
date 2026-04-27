// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Console;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class HelpCommandTests
{
    private readonly TestInteractionService _interaction;
    private readonly FuncRootCommand _rootCommand;
    private readonly HelpCommand _helpCommand;

    public HelpCommandTests()
    {
        _interaction = new TestInteractionService();
        // Use the full Parser to get a real command tree
        _rootCommand = TestParser.CreateRoot(_interaction);
        _helpCommand = _rootCommand.Subcommands.OfType<HelpCommand>().First();
    }

    [Fact]
    public void ShowGeneralHelp_DisplaysProductInfo()
    {
        var exitCode = _helpCommand.ShowGeneralHelp();

        Assert.Equal(0, exitCode);
        Assert.Contains("Azure Functions CLI", _interaction.AllOutput);
        Assert.Contains("Usage", _interaction.AllOutput);
    }

    [Fact]
    public void ShowGeneralHelp_ListsRegisteredCommands()
    {
        _helpCommand.ShowGeneralHelp();

        var output = _interaction.AllOutput;
        Assert.Contains("version", output);
        Assert.Contains("help", output);
    }

    [Fact]
    public void ShowCommandHelp_WithVersionCommand_ShowsInfo()
    {
        var exitCode = _helpCommand.ShowCommandHelp("version");

        Assert.Equal(0, exitCode);
        Assert.Contains("version", _interaction.AllOutput);
    }

    [Fact]
    public void ShowCommandHelp_WithUnknownCommand_ReturnsError()
    {
        var exitCode = _helpCommand.ShowCommandHelp("nonexistent");

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown command", _interaction.AllOutput);
    }
}
