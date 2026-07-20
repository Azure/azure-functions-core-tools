// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Quickstart;
using Azure.Functions.Cli.Quickstart;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands.Quickstart;

public class QuickstartCommandTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly IQuickstartProviderResolver _resolver = Substitute.For<IQuickstartProviderResolver>();
    private readonly IQuickstartManifestService _manifestService = Substitute.For<IQuickstartManifestService>();
    private readonly IQuickstartScaffolder _scaffolder = Substitute.For<IQuickstartScaffolder>();

    private QuickstartCommand CreateCommand(params IQuickstartProvider[] providers)
    {
        var listCmd = new QuickstartListCommand(_interaction, _resolver, _manifestService, providers);
        var infoCmd = new QuickstartInfoCommand(_interaction, _resolver, _manifestService);
        return new QuickstartCommand(listCmd, infoCmd, _interaction, _resolver, _manifestService, _scaffolder, providers);
    }

    [Fact]
    public void QuickstartCommand_HasExpectedOptions()
    {
        QuickstartCommand cmd = CreateCommand();

        var optionNames = cmd.Options.Select(o => o.Name).ToList();

        optionNames.Should().Contain("--stack");
        optionNames.Should().Contain("--language");
        optionNames.Should().Contain("--template");
        optionNames.Should().Contain("--resource");
        optionNames.Should().Contain("--iac");
        optionNames.Should().Contain("--search");
        optionNames.Should().Contain("--fetch");
        optionNames.Should().Contain("--force");
    }

    [Fact]
    public void QuickstartCommand_StackOptionDescription_NoProviders_PointsAtSetup()
    {
        QuickstartCommand cmd = CreateCommand();
        string description = cmd.StackOption.Description ?? string.Empty;

        description.Should().Contain("Set up a stack");
        description.Should().Contain("func setup --features");
    }

    [Fact]
    public void QuickstartCommand_StackOptionDescription_ListsInstalledStacks_SortedAndLowercased()
    {
        QuickstartCommand cmd = CreateCommand(
            QuickstartTestHelpers.CreateProvider(stack: "Python"),
            QuickstartTestHelpers.CreateProvider(stack: "dotnet"),
            QuickstartTestHelpers.CreateProvider(stack: "node"));
        string description = cmd.StackOption.Description ?? string.Empty;

        description.Should().Contain("Supported values: dotnet, node, python.");
    }

    [Fact]
    public void QuickstartCommand_HelpFooterHint_PointsAtWorkloadSearch()
    {
        QuickstartCommand cmd = CreateCommand();

        (cmd.GetHelpFooterHint() ?? string.Empty).Should().Contain("func workload search --stack");
    }

    [Fact]
    public void QuickstartCommand_HasSubcommands()
    {
        QuickstartCommand cmd = CreateCommand();

        var subcommandNames = cmd.Subcommands.Select(c => c.Name).ToList();

        subcommandNames.Should().Contain("list");
        subcommandNames.Should().Contain("info");
    }

    [Fact]
    public void QuickstartCommand_HasPathArgument()
    {
        QuickstartCommand cmd = CreateCommand();

        cmd.Arguments.Should().ContainSingle();
        cmd.Arguments[0].Name.Should().Be("path");
    }

    [Fact]
    public void QuickstartCommand_RegisteredInParser()
    {
        var root = TestParser.CreateRoot(_interaction);
        var names = root.Subcommands.Select(c => c.Name).ToList();

        names.Should().Contain("quickstart");
    }
}
