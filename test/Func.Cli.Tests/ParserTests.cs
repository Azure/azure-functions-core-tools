// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;
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
        var root = TestParser.CreateRoot(_interaction);

        Assert.NotNull(root);
        Assert.IsType<FuncRootCommand>(root);
    }

    [Fact]
    public void CreateCommand_HasExpectedSubcommands()
    {
        var root = TestParser.CreateRoot(_interaction);
        var names = root.Subcommands.Select(c => c.Name).ToList();

        Assert.Contains("version", names);
        Assert.Contains("help", names);
    }

    [Fact]
    public void CreateCommand_HasGlobalOptions()
    {
        var root = TestParser.CreateRoot(_interaction);
        var optionNames = root.Options.Select(o => o.Name).ToList();

        Assert.Contains("--verbose", optionNames);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("help")]
    public void Parse_ValidCommand_DoesNotProduceErrors(string commandName)
    {
        var root = TestParser.CreateRoot(_interaction);
        var result = root.Parse(commandName);

        Assert.Empty(result.Errors);
    }

    // --- Workload command tracking ---

    [Fact]
    public void CreateCommand_WorkloadRegisteredCommand_AppearsAsRootSubcommand()
    {
        var workload = TestWorkloads.CreateInfo("My.Workload");
        var root = TestParser.CreateRootWithWorkload(
            _interaction,
            workload,
            builder => builder.RegisterCommand(new TestWorkloads.StubFuncCommand("workload-cmd", "wd")));

        var added = root.Subcommands.OfType<ExternalCommand>().SingleOrDefault();
        Assert.NotNull(added);
        Assert.Equal("workload-cmd", added!.Name);
        Assert.Same(workload, added.Workload);
    }

    [Fact]
    public void CreateCommand_WorkloadCommandCollidesWithBuiltIn_IsSkippedWithNamedWarning()
    {
        var workload = TestWorkloads.CreateInfo("Wl.Bar");
        var root = TestParser.CreateRootWithWorkload(
            _interaction,
            workload,
            builder => builder.RegisterCommand(new TestWorkloads.StubFuncCommand("init")));

        // init is a built-in name; the workload registration is skipped.
        var initCommands = root.Subcommands.Where(c => c.Name == "init").ToList();
        Assert.Single(initCommands);
        Assert.IsNotType<ExternalCommand>(initCommands[0]);

        Assert.Contains(_interaction.Lines, l => l.StartsWith("WARNING:") && l.Contains("Wl.Bar") && l.Contains("'init'"));
    }

    [Fact]
    public void CreateCommand_TwoWorkloadCommandsSameName_BothSkippedWithNamedWarning()
    {
        var workloadA = TestWorkloads.CreateInfo("Wl.A");
        var workloadB = TestWorkloads.CreateInfo("Wl.B");

        var services = TestParser.BuildServiceProviderWith(_interaction, s =>
        {
            new DefaultFunctionsCliBuilder(s, workloadA)
                .RegisterCommand(new TestWorkloads.StubFuncCommand("dup"));
            new DefaultFunctionsCliBuilder(s, workloadB)
                .RegisterCommand(new TestWorkloads.StubFuncCommand("dup"));
        });

        var root = Parser.CreateCommand(services);

        Assert.DoesNotContain(root.Subcommands, c => c.Name == "dup");
        Assert.Contains(_interaction.Lines, l => l.StartsWith("WARNING:")
            && l.Contains("Wl.A") && l.Contains("Wl.B") && l.Contains("'dup'"));
    }

    [Fact]
    public void CreateCommand_BaseCommandNotBuiltInOrExternal_Throws()
    {
        var services = TestParser.BuildServiceProviderWith(_interaction, s =>
        {
            s.AddSingleton<BaseCommand, RogueBaseCommand>();
        });

        var ex = Assert.Throws<InvalidOperationException>(() => Parser.CreateCommand(services));
        Assert.Contains(nameof(RogueBaseCommand), ex.Message);
        Assert.Contains("rogue", ex.Message);
    }

    [Fact]
    public async Task Parse_BuiltInWorkloadParentCommand_NoSubcommand_RendersHelp()
    {
        // After switching to BaseCommand with the virtual default ExecuteAsync,
        // `func workload` (no subcommand) should print help and exit 0. The
        // default impl walks parent commands to find the root's HelpOption and
        // invokes its (Spectre-wired) action.
        var root = TestParser.CreateRoot(_interaction);
        var parseResult = root.Parse("workload");

        var exit = await parseResult.InvokeAsync();

        Assert.Equal(0, exit);
        Assert.NotEmpty(_interaction.Lines);
    }

    private sealed class RogueBaseCommand : BaseCommand
    {
        public RogueBaseCommand()
            : base("rogue", "A BaseCommand that is neither built-in nor external.")
        {
        }
    }
}
