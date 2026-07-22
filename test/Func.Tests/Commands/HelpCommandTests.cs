// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;

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

        exitCode.Should().Be(0);
        _interaction.AllOutput.Should().Contain("Azure Functions CLI");
        _interaction.AllOutput.Should().Contain("Usage");
    }

    [Fact]
    public void ShowGeneralHelp_ListsRegisteredCommands()
    {
        _helpCommand.ShowGeneralHelp();

        var output = _interaction.AllOutput;
        output.Should().Contain("version");
        output.Should().Contain("help");
    }

    [Fact]
    public void ShowCommandHelp_WithVersionCommand_ShowsInfo()
    {
        var exitCode = _helpCommand.ShowCommandHelp("version");

        exitCode.Should().Be(0);
        _interaction.AllOutput.Should().Contain("version");
    }

    [Fact]
    public void ShowCommandHelp_WithUnknownCommand_ReturnsError()
    {
        var exitCode = _helpCommand.ShowCommandHelp("nonexistent");

        exitCode.Should().Be(1);
        _interaction.AllOutput.Should().Contain("Unknown command");
    }

    [Fact]
    public void RenderCommandHelp_WithNestedSubcommand_UsesFullPath()
    {
        var workloadCommand = _rootCommand.Subcommands
            .First(c => c.Name == "workload");
        var installCommand = workloadCommand.Subcommands
            .First(c => c.Name == "install");

        _helpCommand.RenderCommandHelp(installCommand);

        var output = _interaction.AllOutput;
        output.Should().Contain("func workload install");
        output.Should().NotContain("func install ");
    }
}
