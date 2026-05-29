// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Quickstart;
using Azure.Functions.Cli.Quickstart;
using NSubstitute;
using Xunit;

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

        Assert.Contains("--stack", optionNames);
        Assert.Contains("--language", optionNames);
        Assert.Contains("--template", optionNames);
        Assert.Contains("--resource", optionNames);
        Assert.Contains("--iac", optionNames);
        Assert.Contains("--search", optionNames);
        Assert.Contains("--fetch", optionNames);
        Assert.Contains("--force", optionNames);
    }

    [Fact]
    public void QuickstartCommand_StackOptionDescription_NoProviders_PointsAtWorkloadInstall()
    {
        QuickstartCommand cmd = CreateCommand();
        string description = cmd.StackOption.Description ?? string.Empty;

        Assert.Contains("Install a stack workload", description);
        Assert.Contains("func workload install", description);
    }

    [Fact]
    public void QuickstartCommand_StackOptionDescription_ListsInstalledStacks_SortedAndLowercased()
    {
        QuickstartCommand cmd = CreateCommand(
            QuickstartTestHelpers.CreateProvider(stack: "Python"),
            QuickstartTestHelpers.CreateProvider(stack: "dotnet"),
            QuickstartTestHelpers.CreateProvider(stack: "node"));
        string description = cmd.StackOption.Description ?? string.Empty;

        Assert.Contains("Supported values: dotnet, node, python.", description);
    }

    [Fact]
    public void QuickstartCommand_HelpFooterHint_PointsAtWorkloadSearch()
    {
        QuickstartCommand cmd = CreateCommand();

        Assert.Contains("func workload search --stack", cmd.GetHelpFooterHint() ?? string.Empty);
    }

    [Fact]
    public void QuickstartCommand_HasSubcommands()
    {
        QuickstartCommand cmd = CreateCommand();

        var subcommandNames = cmd.Subcommands.Select(c => c.Name).ToList();

        Assert.Contains("list", subcommandNames);
        Assert.Contains("info", subcommandNames);
    }

    [Fact]
    public void QuickstartCommand_HasPathArgument()
    {
        QuickstartCommand cmd = CreateCommand();

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
