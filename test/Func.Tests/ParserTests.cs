// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

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
        FuncRootCommand root = TestParser.CreateRoot(_interaction);

        root.Should().NotBeNull();
        root.Should().BeOfType<FuncRootCommand>();
    }

    [Fact]
    public void CreateCommand_HasExpectedSubcommands()
    {
        FuncRootCommand root = TestParser.CreateRoot(_interaction);
        var names = root.Subcommands.Select(c => c.Name).ToList();

        names.Should().Contain("version");
        names.Should().Contain("help");
    }

    [Fact]
    public void CreateCommand_HasGlobalOptions()
    {
        FuncRootCommand root = TestParser.CreateRoot(_interaction);
        var optionNames = root.Options.Select(o => o.Name).ToList();

        optionNames.Should().Contain("--verbose");
    }

    [Fact]
    public void VerboseOption_HasNoShortAlias()
    {
        // -v is intentionally left free so subcommands can claim it.
        var root = (FuncRootCommand)TestParser.CreateRoot(_interaction);

        root.VerboseOption.Aliases.Should().NotContain("-v");
    }

    [Theory]
    [InlineData("version")]
    [InlineData("help")]
    public void Parse_ValidCommand_DoesNotProduceErrors(string commandName)
    {
        FuncRootCommand root = TestParser.CreateRoot(_interaction);
        ParseResult result = root.Parse(commandName);

        result.Errors.Should().BeEmpty();
    }

    // --- Workload command tracking ---

    [Fact]
    public void CreateCommand_WorkloadRegisteredCommand_AppearsAsRootSubcommand()
    {
        RuntimeWorkloadInfo workload = TestWorkloads.CreateInfo("My.Workload");
        FuncRootCommand root = TestParser.CreateRootWithWorkload(
            _interaction,
            workload,
            builder => builder.RegisterCommand(new TestWorkloads.StubFuncCommand("workload-cmd", "wd")));

        ExternalCommand? added = root.Subcommands.OfType<ExternalCommand>().SingleOrDefault();
        added.Should().NotBeNull();
        added!.Name.Should().Be("workload-cmd");
        added.Workload.Should().BeSameAs(workload);
    }

    [Fact]
    public void CreateCommand_WorkloadCommandCollidesWithBuiltIn_IsSkippedWithNamedWarning()
    {
        RuntimeWorkloadInfo workload = TestWorkloads.CreateInfo("Wl.Bar");
        FuncRootCommand root = TestParser.CreateRootWithWorkload(
            _interaction,
            workload,
            builder => builder.RegisterCommand(new TestWorkloads.StubFuncCommand("init")));

        // init is a built-in name; the workload registration is skipped.
        var initCommands = root.Subcommands.Where(c => c.Name == "init").ToList();
        initCommands.Should().ContainSingle();
        initCommands[0].Should().NotBeOfType<ExternalCommand>();

        _interaction.Lines.Should().Contain(l => l.StartsWith("WARNING:") && l.Contains("Wl.Bar") && l.Contains("'init'"));
    }

    [Fact]
    public void CreateCommand_TwoWorkloadCommandsSameName_BothSkippedWithNamedWarning()
    {
        RuntimeWorkloadInfo workloadA = TestWorkloads.CreateInfo("Wl.A");
        RuntimeWorkloadInfo workloadB = TestWorkloads.CreateInfo("Wl.B");

        IServiceProvider services = TestParser.BuildServiceProviderWith(_interaction, s =>
        {
            new DefaultFunctionsCliBuilder(s, workloadA)
                .RegisterCommand(new TestWorkloads.StubFuncCommand("dup"));
            new DefaultFunctionsCliBuilder(s, workloadB)
                .RegisterCommand(new TestWorkloads.StubFuncCommand("dup"));
        });

        FuncRootCommand root = Parser.CreateCommand(services);

        root.Subcommands.Should().NotContain(c => c.Name == "dup");
        _interaction.Lines.Should().Contain(l => l.StartsWith("WARNING:")
            && l.Contains("Wl.A") && l.Contains("Wl.B") && l.Contains("'dup'"));
    }

    [Fact]
    public void CreateCommand_FuncCliCommandNotBuiltInOrExternal_Throws()
    {
        IServiceProvider services = TestParser.BuildServiceProviderWith(_interaction, s =>
        {
            s.AddSingleton<FuncCliCommand, RogueFuncCliCommand>();
        });

        InvalidOperationException ex = FluentActions.Invoking(() => Parser.CreateCommand(services)).Should().ThrowExactly<InvalidOperationException>().Which;
        ex.Message.Should().Contain(nameof(RogueFuncCliCommand));
        ex.Message.Should().Contain("rogue");
    }

    [Fact]
    public async Task Parse_BuiltInWorkloadParentCommand_NoSubcommand_RendersHelp()
    {
        // After switching to FuncCliCommand with the virtual default ExecuteAsync,
        // `func workload` (no subcommand) should print help and exit 0. The
        // default impl walks parent commands to find the root's HelpOption and
        // invokes its (Spectre-wired) action.
        FuncRootCommand root = TestParser.CreateRoot(_interaction);
        ParseResult parseResult = root.Parse("workload");

        int exit = await parseResult.InvokeAsync();

        exit.Should().Be(0);
        _interaction.Lines.Should().NotBeEmpty();
    }

    private sealed class RogueFuncCliCommand : FuncCliCommand
    {
        public RogueFuncCliCommand()
            : base("rogue", "A FuncCliCommand that is neither built-in nor external.")
        {
        }
    }
}
