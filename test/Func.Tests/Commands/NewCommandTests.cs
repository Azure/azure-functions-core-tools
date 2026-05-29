// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Templates;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class NewCommandTests
{
    private readonly TestInteractionService _interaction;
    private readonly NewCommandRunner _runner;

    public NewCommandTests()
    {
        _interaction = new TestInteractionService();
        _runner = new NewCommandRunner(
            _interaction,
            Substitute.For<Cli.Projects.IFunctionsProjectResolver>(),
            Substitute.For<Cli.Profiles.IProfileResolver>(),
            Substitute.For<Microsoft.Extensions.Options.IOptionsMonitor<Cli.Configuration.StackOptions>>(),
            System.Array.Empty<Cli.Projects.IProjectInitializer>(),
            Substitute.For<IInstalledTemplatesWorkloads>(),
            new TemplateEngineProviderRegistry([]),
            new TemplateOptionHydrator(System.Array.Empty<Cli.Projects.IProjectInitializer>()),
            new TemplatePicker(_interaction),
            new NewCommandRenderer(_interaction),
            Substitute.For<Cli.Bundles.IHostJsonBundleSectionReader>(),
            Substitute.For<Cli.Bundles.IExtensionBundleResolver>());
    }

    [Fact]
    public void NewCommand_HasExpectedOptions()
    {
        var cmd = new NewCommand(_runner);
        var optionNames = cmd.Options.Select(o => o.Name).ToList();

        Assert.Contains("--name", optionNames);
        Assert.Contains("--template", optionNames);
        Assert.Contains("--force", optionNames);
        Assert.Contains("--non-interactive", optionNames);
        Assert.Contains("--list", optionNames);
    }

    [Fact]
    public void NewCommand_RegisteredInParser()
    {
        var root = TestParser.CreateRoot(_interaction);
        var names = root.Subcommands.Select(c => c.Name).ToList();

        Assert.Contains("new", names);
    }

    [Fact]
    public void NewCommand_HasPathArgument()
    {
        var cmd = new NewCommand(_runner);
        Assert.Single(cmd.Arguments);
        Assert.Equal("path", cmd.Arguments[0].Name);
    }

    // Single-dash typos like `-name` must surface as unrecognized options, not "needs a project".
    [Fact]
    public void NewCommand_SingleDashLongOption_ReportsUnrecognizedOption()
    {
        var root = TestParser.CreateRoot(_interaction);

        ParseResult result = root.Parse(
            new[] { "new", "--template", "HttpTrigger-Python", "-name", "ttpt" },
            new ParserConfiguration { EnablePosixBundling = false });

        Assert.Contains(
            result.Errors,
            e => e.Message.Contains("Unrecognized option '-name'", System.StringComparison.Ordinal));
    }
}

