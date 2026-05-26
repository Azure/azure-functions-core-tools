// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Templates;
using Azure.Functions.Cli.Templates;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class NewCommandTests
{
    private readonly TestInteractionService _interaction;
    private readonly RecordingWorkloadHintRenderer _hintRenderer;

    public NewCommandTests()
    {
        _interaction = new TestInteractionService();
        _hintRenderer = new RecordingWorkloadHintRenderer();
    }

    [Fact]
    public void NewCommand_HasExpectedOptions()
    {
        NewCommand cmd = MakeNewCommand();
        List<string> optionNames = [..cmd.Options.Select(o => o.Name)];

        Assert.Contains("--name", optionNames);
        Assert.Contains("--template", optionNames);
        Assert.Contains("--force", optionNames);
    }

    [Fact]
    public void NewCommand_RegisteredInParser()
    {
        FuncRootCommand root = TestParser.CreateRoot(_interaction);
        List<string> names = [..root.Subcommands.Select(c => c.Name)];

        Assert.Contains("new", names);
    }

    [Fact]
    public void NewCommand_HasPathArgument()
    {
        NewCommand cmd = MakeNewCommand();
        Assert.Single(cmd.Arguments);
        Assert.Equal("path", cmd.Arguments[0].Name);
    }

    [Fact]
    public void NewCommand_HasTemplatesSubcommand()
    {
        NewCommand cmd = MakeNewCommand();
        Assert.Contains(cmd.Subcommands, s => s.Name == "templates");
    }

    private static NewCommand MakeNewCommand()
    {
        TestInteractionService interaction = new();
        TemplatesListCommand listCmd = new(interaction, Substitute.For<ITemplateManifestClient>());
        TemplatesInfoCommand infoCmd = new(interaction, Substitute.For<ITemplateManifestClient>());
        TemplatesCommand templatesCmd = new(
            listCmd,
            infoCmd,
            interaction,
            Substitute.For<ITemplateManifestClient>(),
            Substitute.For<ITemplateFunctionScaffolder>());
        return new NewCommand(new RecordingWorkloadHintRenderer(), templatesCmd);
    }
}
