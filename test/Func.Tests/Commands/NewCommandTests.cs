// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
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

    [Theory]
    [InlineData("--name")]
    [InlineData("-n")]
    [InlineData("--trigger-function-name")]
    public void NameAlias_UsesExistingNameOptionAndReservesOneHostValue(string alias)
    {
        var command = new NewCommand(_runner, _templateOptions);
        FuncRootCommand root = [command];

        var parsed = root.Parse(["new", alias, "NamedTimer"], new() { EnablePosixBundling = false });

        parsed.Errors.Should().BeEmpty();
        parsed.GetValue(command.NameOption).Should().Be("NamedTimer");
        command.Options.Where(option => option.Name == alias || option.Aliases.Contains(alias)).Should().Equal(command.NameOption);
    }

    [Theory]
    [InlineData("--name", "-n")]
    [InlineData("--trigger-function-name", "--name")]
    [InlineData("-n", "--trigger-function-name")]
    [InlineData("--trigger-function-name", "--trigger-function-name")]
    public void NameAlias_DuplicateSpellings_UseExistingSingleValueFailure(string first, string second)
    {
        var command = new NewCommand(_runner, _templateOptions);
        FuncRootCommand root = [command];

        var parsed = root.Parse(["new", first, "First", second, "Second"], new() { EnablePosixBundling = false });

        parsed.Errors.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NameAlias_HydrationCannotShadowBuiltinOrAccumulateAcrossHelp(bool preparse, bool aliasOnly)
    {
        var command = new NewCommand(_runner, _templateOptions);
        var legacyName = aliasOnly ? new Option<string?>("--legacy-name", "--trigger-function-name") : new Option<string?>("--trigger-function-name");
        var other = new Option<string?>("--timer-trigger-schedule");
        IReadOnlyList<Option> options = [legacyName, other];
        IReadOnlyList<HydratedTemplateOption> mapped =
        [
            new(legacyName, "trigger-functionName"),
            new(other, "timerTrigger-schedule"),
        ];
        _templateOptions.HydrateOptionsForTemplateAsync(Arg.Any<NewInvocation>(), "Timer", Arg.Any<CancellationToken>())
            .Returns(options);
        _templateOptions.HydrateOptionsForTemplateWithIdsAsync(Arg.Any<NewInvocation>(), "Timer", Arg.Any<CancellationToken>())
            .Returns(mapped);
        FuncRootCommand root = [command];

        if (preparse)
        {
            command.AttachHydratedOptionsForPreParse("Timer");
            command.AttachHydratedOptionsForPreParse("Timer");
        }
        else
        {
            var parsed = root.Parse(["new", "--template", "Timer"]);
            command.AttachHydratedOptionsForHelp(parsed).Should().Be(1);
            command.AttachHydratedOptionsForHelp(parsed).Should().Be(1);
        }

        command.Options.Should().NotContain(legacyName).And.Contain(other).And.Contain(command.NameOption);
        command.GetTemplateHelpInfo()!.HydratedOptions.Should().Equal(other);
        var named = root.Parse(["new", "--template", "Timer", "--trigger-function-name", "NamedTimer"]);
        named.Errors.Should().BeEmpty();
        named.GetValue(command.NameOption).Should().Be("NamedTimer");
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
