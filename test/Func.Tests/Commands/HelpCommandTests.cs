// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Workloads;

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

    [Fact]
    public void CommandGraph_AllBuiltInMetadata_HasSingleLineDescriptions()
    {
        HelpMetadataValidator validator = new();

        IReadOnlyList<string> violations = validator.FindViolations(_rootCommand);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void CommandGraph_WorkloadMetadata_HasSingleLineDescriptions()
    {
        FuncCommandOption<string> output = new("--output", "-o", "Output directory.");
        FuncCommandArgument<string> target = new("target", "Deployment target.", isRequired: true);
        var deploy = new TestWorkloads.StubFuncCommand(
            "deploy",
            "Deploy the application.",
            options: [output],
            arguments: [target]);
        var cloud = new TestWorkloads.StubFuncCommand(
            "cloud",
            "Manage cloud deployments.",
            subcommands: [deploy]);
        FuncRootCommand root = TestParser.CreateRootWithWorkload(
            _interaction,
            TestWorkloads.CreateInfo(),
            builder => builder.RegisterCommand(cloud));
        HelpMetadataValidator validator = new();

        IReadOnlyList<string> violations = validator.FindViolations(root);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void FindViolations_HiddenCommandHasBlankDescription_ReportsFullCommandPath()
    {
        RootCommand root = new("Root command.");
        var command = new Command("internal", string.Empty) { Hidden = true };
        root.Subcommands.Add(command);
        HelpMetadataValidator validator = new();

        IReadOnlyList<string> violations = validator.FindViolations(root);

        violations.Should().ContainSingle().Subject.Should().Be("func internal: command");
    }

    [Fact]
    public void FindViolations_OptionHasWhitespaceDescription_ReportsFullOptionPath()
    {
        RootCommand root = new("Root command.");
        var command = new Command("deploy", "Deploy the application.");
        command.Options.Add(new Option<bool>("--force") { Description = " " });
        root.Subcommands.Add(command);
        HelpMetadataValidator validator = new();

        IReadOnlyList<string> violations = validator.FindViolations(root);

        violations.Should().ContainSingle().Subject.Should().Be("func deploy --force: option");
    }

    [Fact]
    public void FindViolations_ArgumentHasMultilineDescription_ReportsFullArgumentPath()
    {
        RootCommand root = new("Root command.");
        var command = new Command("deploy", "Deploy the application.");
        command.Arguments.Add(new Argument<string>("target") { Description = "First line.\nSecond line." });
        root.Subcommands.Add(command);
        HelpMetadataValidator validator = new();

        IReadOnlyList<string> violations = validator.FindViolations(root);

        violations.Should().ContainSingle().Subject.Should().Be("func deploy <target>: argument");
    }

    [Fact]
    public void FindViolations_OptionDescriptionHasCarriageReturn_ReportsFullOptionPath()
    {
        RootCommand root = new("Root command.");
        var command = new Command("deploy", "Deploy the application.");
        command.Options.Add(new Option<bool>("--force") { Description = "First line.\rSecond line." });
        root.Subcommands.Add(command);
        HelpMetadataValidator validator = new();

        IReadOnlyList<string> violations = validator.FindViolations(root);

        violations.Should().ContainSingle().Subject.Should().Be("func deploy --force: option");
    }
}
