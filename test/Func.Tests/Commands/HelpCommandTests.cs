// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

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
    public void ShowCommandHelp_WithNullCommandPath_Throws()
    {
        var act = () => _helpCommand.ShowCommandHelp(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("commandPath");
    }

    [Fact]
    public void ShowCommandHelp_WithEmptyCommandPath_ShowsGeneralHelp()
    {
        var exitCode = _helpCommand.ShowCommandHelp();

        exitCode.Should().Be(0);
        _interaction.AllOutput.Should().Contain("TITLE: Azure Functions CLI");
    }

    [Theory]
    [InlineData("")]
    [InlineData("help")]
    public async Task Help_WithoutCommandPath_ShowsGeneralHelp(string commandLine)
    {
        var exitCode = await _rootCommand.Parse(commandLine).InvokeAsync();

        exitCode.Should().Be(0);
        _interaction.AllOutput.Should().Contain("TITLE: Azure Functions CLI").And.Contain("RULE: Commands");
    }

    [Theory]
    [InlineData("version")]
    [InlineData("workload")]
    [InlineData("workload install")]
    [InlineData("workload list")]
    [InlineData("profile show")]
    [InlineData("start")]
    public async Task Help_WithCommandPath_MatchesHelpOption(string commandPath)
    {
        var expectedInteraction = new TestInteractionService();
        var expectedRoot = TestParser.CreateRoot(expectedInteraction);
        var expectedExitCode = await expectedRoot.Parse($"{commandPath} --help").InvokeAsync();

        var exitCode = await _rootCommand.Parse($"help {commandPath}").InvokeAsync();

        expectedExitCode.Should().Be(0);
        exitCode.Should().Be(0);
        _interaction.Lines.Should().Equal(expectedInteraction.Lines);
    }

    [Theory]
    [InlineData("nonexistent")]
    [InlineData("workload nonexistent")]
    [InlineData("nonexistent install")]
    [InlineData("version install")]
    [InlineData("workload install nonexistent")]
    public async Task Help_WithInvalidCommandPath_ReturnsErrorWithoutRenderingPartialHelp(string commandPath)
    {
        var exitCode = await _rootCommand.Parse($"help {commandPath}").InvokeAsync();

        exitCode.Should().Be(1);
        _interaction.Lines.Should().ContainSingle()
            .Which.Should().Be($"ERROR: Unknown command: '{commandPath}'. Run 'func help' for available commands.");
    }

    [Theory]
    [InlineData("cloud deployment create")]
    [InlineData("CLOUD DEPLOYMENT CREATE")]
    [InlineData("cloud deploy add")]
    public async Task Help_WithDeepCommandPath_ResolvesCommandMetadataWithoutExecuting(string commandPath)
    {
        var executed = false;
        var create = new Command("create", "Create a deployment.");
        create.Aliases.Add("add");
        create.Arguments.Add(new Argument<string>("name") { Description = "Deployment name." });
        create.SetAction(_ =>
        {
            executed = true;
            return 0;
        });
        var deployment = new Command("deployment", "Manage deployments.");
        deployment.Aliases.Add("deploy");
        deployment.Subcommands.Add(create);
        var cloud = new Command("cloud", "Manage cloud resources.");
        cloud.Subcommands.Add(deployment);
        _rootCommand.Subcommands.Add(cloud);

        var exitCode = await _rootCommand.Parse($"help {commandPath}").InvokeAsync();

        exitCode.Should().Be(0);
        executed.Should().BeFalse();
        _interaction.AllOutput.Should().Contain("TITLE: func cloud deployment create")
            .And.Contain("Create a deployment.").And.Contain("<name>");
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

    [Theory]
    [InlineData("init --help")]
    [InlineData("init -h")]
    [InlineData("help init")]
    public async Task InitHelp_GroupsStackOptionsAlphabeticallyAndKeepsSharedOptionsGeneral(string commandLine)
    {
        var first = CreateInitializer("first", "first stack", registry =>
        [
            registry.GetOrAdd(new Option<bool>("--first-only", "-f")),
            registry.GetOrAdd(CommonInitOptions.NoBundle()),
            registry.GetOrAdd(CommonInitOptions.BundlesChannel()),
            registry.GetOrAdd(new Option<bool>("--shared")),
        ]);
        var second = CreateInitializer("second", "Second stack", registry =>
        [
            registry.GetOrAdd(new Option<bool>("--second-only")),
            registry.GetOrAdd(CommonInitOptions.NoBundle()),
            registry.GetOrAdd(new Option<bool>("--shared")),
        ]);
        var hidden = CreateInitializer("hidden", "Hidden stack", registry =>
        [
            registry.GetOrAdd(CommonInitOptions.NoBundle()),
            registry.GetOrAdd(new Option<bool>("--shared")),
            registry.GetOrAdd(new Option<bool>("--hidden") { Hidden = true }),
        ]);
        var provider = TestParser.BuildServiceProviderWith(_interaction, services =>
        {
            services.AddSingleton(second);
            services.AddSingleton(first);
            services.AddSingleton(hidden);
        });
        var root = Parser.CreateCommand(provider);

        var exitCode = await root.Parse(commandLine).InvokeAsync();

        exitCode.Should().Be(0);
        _interaction.Lines.Should().ContainInOrder("RULE: Options", "RULE: first stack", "RULE: Second stack");
        GetHelpSection("Options").Should().Contain("--stack").And.Contain("--no-bundles")
            .And.Contain("--bundles-channel, -c").And.Contain("--shared")
            .And.NotContain("--first-only").And.NotContain("--second-only");
        GetHelpSection("first stack").Should().Contain("--first-only, -f").And.NotContain("--shared")
            .And.NotContain("--second-only");
        GetHelpSection("Second stack").Should().Contain("--second-only").And.NotContain("--shared")
            .And.NotContain("--first-only");
        _interaction.Lines.Count(line => line.StartsWith("  --no-bundles", StringComparison.Ordinal)).Should().Be(1);
        _interaction.Lines.Count(line => line.StartsWith("  --shared", StringComparison.Ordinal)).Should().Be(1);
        _interaction.AllOutput.Should().NotContain("RULE: Hidden stack").And.NotContain("--hidden");
    }

    [Theory]
    [InlineData("dotnet", ".NET")]
    [InlineData("node", "Node.js")]
    [InlineData("go", "Go")]
    [InlineData("powershell", "PowerShell")]
    public async Task InitHelp_WithOneStack_UsesDisplayNameAndKeepsBundleOptionsGeneral(string stackId, string displayName)
    {
        var initializer = CreateInitializer(stackId, displayName, registry =>
        [
            registry.GetOrAdd(CommonInitOptions.NoBundle()),
            registry.GetOrAdd(CommonInitOptions.BundlesChannel()),
            registry.GetOrAdd(new Option<bool>("--stack-specific")),
        ]);
        var provider = TestParser.BuildServiceProviderWith(_interaction, services => services.AddSingleton(initializer));
        var root = Parser.CreateCommand(provider);

        var exitCode = await root.Parse("init --help").InvokeAsync();

        exitCode.Should().Be(0);
        _interaction.Lines.Should().ContainInOrder("RULE: Options", $"RULE: {displayName}");
        GetHelpSection("Options").Should().Contain("--no-bundles").And.Contain("--bundles-channel, -c")
            .And.NotContain("--stack-specific");
        GetHelpSection(displayName).Should().Contain("--stack-specific").And.NotContain("--no-bundles")
            .And.NotContain("--bundles-channel");
    }

    [Theory]
    [InlineData("run --help")]
    [InlineData("start --help")]
    [InlineData("run -h")]
    [InlineData("help run")]
    public async Task RunHelp_GroupsStackOptionsAlphabeticallyAndKeepsBuiltInAndSharedOptionsGeneral(string commandLine)
    {
        var dotnet = CreateStartContributor("dotnet", registry =>
        [
            registry.GetOrAdd(new Option<bool>("--first-only")),
            registry.GetOrAdd(new Option<int?>("--port")),
            registry.GetOrAdd(new Option<bool>("--shared")),
        ]);
        var node = CreateStartContributor("node", registry =>
        [
            registry.GetOrAdd(new Option<bool>("--second-only")),
            registry.GetOrAdd(new Option<bool>("--shared")),
        ]);
        var dotnetStack = Substitute.For<IProjectStack>();
        dotnetStack.Stack.Returns("dotnet");
        dotnetStack.DisplayName.Returns(".NET");
        var nodeStack = Substitute.For<IProjectStack>();
        nodeStack.Stack.Returns("node");
        nodeStack.DisplayName.Returns("Node.js");
        var provider = TestParser.BuildServiceProviderWith(_interaction, services =>
        {
            services.AddSingleton(node);
            services.AddSingleton(dotnet);
            services.AddSingleton(nodeStack);
            services.AddSingleton(dotnetStack);
        });
        var root = Parser.CreateCommand(provider);

        var exitCode = await root.Parse(commandLine).InvokeAsync();

        exitCode.Should().Be(0);
        _interaction.Lines.Should().ContainInOrder("RULE: Options", "RULE: .NET", "RULE: Node.js");
        GetHelpSection("Options").Should().Contain("--port").And.Contain("--shared")
            .And.NotContain("--first-only").And.NotContain("--second-only");
        GetHelpSection(".NET").Should().Contain("--first-only").And.NotContain("--port").And.NotContain("--shared")
            .And.NotContain("--second-only");
        GetHelpSection("Node.js").Should().Contain("--second-only").And.NotContain("--shared").And.NotContain("--first-only");
        _interaction.Lines.Count(line => line.StartsWith("  --port", StringComparison.Ordinal)).Should().Be(1);
        _interaction.Lines.Count(line => line.StartsWith("  --shared", StringComparison.Ordinal)).Should().Be(1);
    }

    [Theory]
    [InlineData("init")]
    [InlineData("run")]
    [InlineData("new")]
    public void RenderCommandHelp_WithoutStackOptions_OnlyShowsGeneralOptions(string commandName)
    {
        var command = _rootCommand.Subcommands.Single(command => command.Name == commandName);

        _helpCommand.RenderCommandHelp(command);

        _interaction.Lines.Where(line => line.StartsWith("RULE: ", StringComparison.Ordinal))
            .Should().EndWith("RULE: Options");
        _interaction.AllOutput.Should().NotContain("RULE: .NET").And.NotContain("RULE: Node.js");
    }

    [Fact]
    public void GetOptionHelpHeading_NullOption_Throws()
    {
        var act = () => _helpCommand.GetOptionHelpHeading(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("option");
    }

    private string GetHelpSection(string heading) => string.Join(Environment.NewLine, _interaction.Lines
        .SkipWhile(line => line != $"RULE: {heading}")
        .Skip(1)
        .TakeWhile(line => !line.StartsWith("RULE: ", StringComparison.Ordinal)));

    private static IProjectInitializer CreateInitializer(
        string stack, string displayName, Func<IInitOptionRegistry, IReadOnlyList<Option>> options)
    {
        var initializer = Substitute.For<IProjectInitializer>();
        initializer.Stack.Returns(stack);
        initializer.DisplayName.Returns(displayName);
        initializer.GetInitOptions(Arg.Any<IInitOptionRegistry>())
            .Returns(call => options(call.Arg<IInitOptionRegistry>()));
        return initializer;
    }

    private static IStartHostOptionContributor CreateStartContributor(
        string stack, Func<StartOptionRegistry, IReadOnlyList<Option>> options)
    {
        var contributor = Substitute.For<IStartHostOptionContributor>();
        contributor.Stack.Returns(stack);
        contributor.GetStartOptions(Arg.Any<StartOptionRegistry>())
            .Returns(call => options(call.Arg<StartOptionRegistry>()));
        return contributor;
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
