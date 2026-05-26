// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Quickstart;
using Azure.Functions.Cli.Quickstart;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Quickstart;

public class QuickstartCommandTests
{
    private readonly TestInteractionService _interaction = new();

    [Fact]
    public void QuickstartCommand_HasExpectedOptions()
    {
        var listCmd = new QuickstartListCommand(_interaction, []);
        var infoCmd = new QuickstartInfoCommand(_interaction, []);
        var cmd = new QuickstartCommand(listCmd, infoCmd, _interaction, []);

        var optionNames = cmd.Options.Select(o => o.Name).ToList();

        Assert.Contains("--language", optionNames);
        Assert.Contains("--template", optionNames);
        Assert.Contains("--resource", optionNames);
        Assert.Contains("--iac", optionNames);
        Assert.Contains("--search", optionNames);
        Assert.Contains("--fetch", optionNames);
    }

    [Fact]
    public void QuickstartCommand_HasSubcommands()
    {
        var listCmd = new QuickstartListCommand(_interaction, []);
        var infoCmd = new QuickstartInfoCommand(_interaction, []);
        var cmd = new QuickstartCommand(listCmd, infoCmd, _interaction, []);

        var subcommandNames = cmd.Subcommands.Select(c => c.Name).ToList();

        Assert.Contains("list", subcommandNames);
        Assert.Contains("info", subcommandNames);
    }

    [Fact]
    public void QuickstartCommand_HasPathArgument()
    {
        var listCmd = new QuickstartListCommand(_interaction, []);
        var infoCmd = new QuickstartInfoCommand(_interaction, []);
        var cmd = new QuickstartCommand(listCmd, infoCmd, _interaction, []);

        Assert.Single(cmd.Arguments);
        Assert.Equal("path", cmd.Arguments[0].Name);
    }

    [Fact]
    public void QuickstartCommand_RegisteredInParser()
    {
        var root = TestParser.CreateRoot(_interaction);
        var names = root.Subcommands.Select(c => c.Name).ToList();

        Assert.Contains("quickstart", names);
    }
}
