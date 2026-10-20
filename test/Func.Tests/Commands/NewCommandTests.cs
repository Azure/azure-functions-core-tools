// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Invocation;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Templates;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands;

public class NewCommandTests
{
    private readonly TestInteractionService _interaction;
    private readonly INewCommandRunner _runner;
    private readonly INewCommandTemplateOptionProvider _templateOptions;

    public NewCommandTests()
    {
        _interaction = new TestInteractionService();
        _runner = Substitute.For<INewCommandRunner>();
        _templateOptions = Substitute.For<INewCommandTemplateOptionProvider>();
    }

    [Fact]
    public void NewCommand_HasExpectedOptions()
    {
        var cmd = new NewCommand(_runner, _templateOptions);
        var optionNames = cmd.Options.Select(o => o.Name).ToList();

        optionNames.Should().Contain("--name");
        optionNames.Should().Contain("--template");
        optionNames.Should().Contain("--force");
        optionNames.Should().Contain("--non-interactive");
        optionNames.Should().Contain("--list");
        optionNames.Should().Contain("--format");
        optionNames.Should().NotContain("--output");
    }

    [Theory]
    [InlineData("plain", false)]
    [InlineData("json", true)]
    public async Task NewCommand_Format_ForwardsRenderingMode(string format, bool expectedJsonOutput)
    {
        var command = new NewCommand(_runner, _templateOptions);
        _runner.ListAsync(Arg.Any<NewInvocation>(), Arg.Any<CancellationToken>()).Returns(0);
        ParseResult result = command.Parse(["--list", "--format", format]);

        int exitCode = await result.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        exitCode.Should().Be(0);
        await _runner.Received(1).ListAsync(
            Arg.Is<NewInvocation>(invocation => invocation.JsonOutput == expectedJsonOutput),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("--output", "json")]
    [InlineData("--output=json")]
    public void NewCommand_RemovedOutputOption_ReportsUnrecognizedOption(params string[] args)
    {
        var command = new NewCommand(_runner, _templateOptions);
        ParseResult result = command.Parse(["--list", .. args]);

        result.Errors.Should().Contain(error =>
            error.Message.Contains("Unrecognized", StringComparison.Ordinal)
            && error.Message.Contains("--output", StringComparison.Ordinal));
    }

    [Fact]
    public void NewCommand_RegisteredInParser()
    {
        var root = TestParser.CreateRoot(_interaction);
        var names = root.Subcommands.Select(c => c.Name).ToList();

        names.Should().Contain("new");
    }

    [Fact]
    public void NewCommand_HasPathArgument()
    {
        var cmd = new NewCommand(_runner, _templateOptions);
        cmd.Arguments.Should().ContainSingle();
        cmd.Arguments[0].Name.Should().Be("path");
    }

    // Single-dash typos like `-name` must surface as unrecognized options, not "needs a project".
    [Fact]
    public void NewCommand_SingleDashLongOption_ReportsUnrecognizedOption()
    {
        var root = TestParser.CreateRoot(_interaction);

        ParseResult result = root.Parse(
            new[] { "new", "--template", "HttpTrigger-Python", "-name", "ttpt" },
            new ParserConfiguration { EnablePosixBundling = false });

        result.Errors.Should().Contain(e => e.Message.Contains("Unrecognized option '-name'", System.StringComparison.Ordinal));
    }
}
