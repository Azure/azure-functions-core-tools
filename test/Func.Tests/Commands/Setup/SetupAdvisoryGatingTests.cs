// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Common;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public sealed class SetupAdvisoryGatingTests
{
    public static TheoryData<string[], bool> ParsedCommands => new()
    {
        { ["setup"], false },
        { ["setup", "--format", "plain"], false },
        { ["setup", "--format", " PlAiN "], false },
        { ["setup", "--format", " "], false },
        { ["setup", "--check=false"], false },
        { ["setup", "--check=false", "--format=plain", "--non-interactive", "-y"], false },
        { ["setup", "--check"], true },
        { ["setup", "--check=true"], true },
        { ["setup", "--format=plain", "--check"], true },
        { ["setup", "--format", "json"], true },
        { ["setup", "--format=JSON"], true },
        { ["setup", "--format", " \tJsOn\t "], true },
        { ["setup", "--check=false", "--format=json"], true },
        { ["setup", "--check", "--format=json"], true },
        { ["--verbose", "setup", "--format=json", "--features=host", "--install-policy=if-needed", "-y"], true },
        { ["setup", "--yes", "--features", "host", "--format", "json", "--verbose"], true },
        { ["setup", ".", "--check", "--format=plain"], true },
        { ["setup", "--format=json", ".", "--prerelease=false"], true },
        { ["setup", "--source", "json", "--profile", "json", "--features", "host"], false },
        { ["setup", "--format=jsonl"], false },
        { ["setup", "--format=jsonl", "--check"], true },
        { ["setup", "--format=json", "--install-policy=invalid"], true },
        { ["setup", "--help", "--check"], true },
        { ["--help"], false },
        { ["--version"], false },
        { ["--verbose"], false },
        { ["help", "setup"], false },
        { ["workload", "list"], false },
    };

    [Theory]
    [MemberData(nameof(ParsedCommands))]
    public void ShouldSuppressAdvisories_ParsedCommand_UsesSetupOptions(string[] args, bool expected)
    {
        var root = TestParser.CreateRoot(new TestInteractionService());
        var parsed = root.Parse(args, new ParserConfiguration { EnablePosixBundling = false });
        parsed.Errors.Should().BeEmpty();

        bool suppress = SetupCommand.ShouldSuppressAdvisories(parsed);

        suppress.Should().Be(expected);
        parsed.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ShouldSuppressAdvisories_CommandAndOptionAliases_UsesParsedSymbols()
    {
        var root = TestParser.CreateRoot(new TestInteractionService());
        var setup = root.Subcommands.OfType<SetupCommand>().Single();
        setup.Aliases.Add("prepare");
        setup.CheckOption.Aliases.Add("-c");
        setup.FormatOption.Aliases.Add("-f");

        (string[] Args, bool Expected)[] invocations =
        [
            (["prepare", "-c"], true),
            (["prepare", "-f=JSON", "-y"], true),
            (["--verbose", "prepare", "-y", "-f", " json "], true),
            (["prepare", "-c=false", "-f=plain", "-y"], false),
        ];
        foreach (var (args, expected) in invocations)
        {
            var parsed = root.Parse(args, new ParserConfiguration { EnablePosixBundling = false });
            parsed.Errors.Should().BeEmpty();
            parsed.CommandResult.Command.Should().BeSameAs(setup);

            SetupCommand.ShouldSuppressAdvisories(parsed).Should().Be(expected);
            parsed.Errors.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("--format", "jsonl", false)]
    [InlineData("--install-policy", "invalid", true)]
    public async Task ShouldSuppressAdvisories_InvalidOptionValue_LeavesValidationToInvocation(string option, string value, bool expected)
    {
        var runner = Substitute.For<ISetupRunner>();
        using var services = (ServiceProvider)TestParser.BuildServiceProviderWith(
            new TestInteractionService(), registrations => registrations.AddSingleton(runner));
        var root = Parser.CreateCommand(services);
        string[] args = option == "--format"
            ? ["setup", option, value]
            : ["setup", "--format=json", option, value];
        var parsed = root.Parse(args, new ParserConfiguration { EnablePosixBundling = false });
        parsed.Errors.Should().BeEmpty();
        using CancellationTokenSource cancellation = new();

        SetupCommand.ShouldSuppressAdvisories(parsed).Should().Be(expected);
        var error = await FluentActions.Awaiting(() => parsed.InvokeAsync(
            new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellation.Token))
            .Should().ThrowAsync<GracefulException>();

        error.Which.Message.Should().Contain(option).And.Contain(value);
        await runner.DidNotReceive().RunAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("--check=invalid")]
    [InlineData("--check=invalid", "--format=json")]
    public void ShouldSuppressAdvisories_NonBooleanCheckValue_PreservesParserPathBinding(params string[] args)
    {
        var root = TestParser.CreateRoot(new TestInteractionService());
        var parsed = root.Parse(["setup", .. args], new ParserConfiguration { EnablePosixBundling = false });
        var setup = root.Subcommands.OfType<SetupCommand>().Single();

        bool suppress = SetupCommand.ShouldSuppressAdvisories(parsed);

        // SCL treats the non-boolean token as the optional path, not as a boolean conversion error.
        suppress.Should().BeTrue();
        parsed.GetValue(setup.CheckOption).Should().BeTrue();
        parsed.GetValue(setup.PathArgument!)!.OriginalPath.Should().Be("invalid");
        parsed.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, "--format")]
    [InlineData(true, "--check", "--format")]
    [InlineData(true, "--format=json", "--bad-option")]
    public async Task ShouldSuppressAdvisories_MalformedOption_PreservesParserErrors(bool expected, params string[] args)
    {
        var root = TestParser.CreateRoot(new TestInteractionService());
        var parsed = root.Parse(["setup", .. args], new ParserConfiguration { EnablePosixBundling = false });
        var errors = parsed.Errors.ToArray();
        errors.Should().NotBeEmpty();
        using StringWriter output = new();
        using StringWriter errorOutput = new();
        using CancellationTokenSource cancellation = new();

        SetupCommand.ShouldSuppressAdvisories(parsed).Should().Be(expected);
        parsed.Errors.Should().Equal(errors);
        int exitCode = await parsed.InvokeAsync(new InvocationConfiguration
        {
            EnableDefaultExceptionHandler = false,
            Output = output,
            Error = errorOutput,
        }, cancellation.Token);

        exitCode.Should().Be(1);
        parsed.Errors.Should().Equal(errors);
        foreach (var error in errors)
        {
            errorOutput.ToString().Should().Contain(error.Message);
        }
    }

    [Fact]
    public void ShouldSuppressAdvisories_NullParseResult_ThrowsArgumentNullException()
    {
        FluentActions.Invoking(() => SetupCommand.ShouldSuppressAdvisories(null!))
            .Should().ThrowExactly<ArgumentNullException>().WithParameterName("parseResult");
    }

    [Theory]
    [InlineData(false, 0, "--version", "setup", "--format")]
    [InlineData(false, 0, "--version", "setup", "--check=false", "--format")]
    [InlineData(true, 0, "--version", "setup", "--check=true", "--format")]
    [InlineData(false, 0, "--version", "prepare", "-f")]
    [InlineData(false, 0, "--help", "setup", "--format")]
    [InlineData(false, 1, "setup", "--format")]
    [InlineData(true, 0, "--version", "setup", "--format=json")]
    [InlineData(false, 0, "--version", "setup", "--install-policy")]
    [InlineData(false, 0, "--version")]
    [InlineData(false, 0, "--version", "setup", "--format=json", "--format=plain")]
    [InlineData(false, 0, "--version", "setup", "--check=true", "--check=false")]
    [InlineData(true, 0, "--version", "setup", "--check=true", "--check=false", "--format=json")]
    [InlineData(false, 0, "--version", "setup", "--check=true", "--check=false", "--format")]
    [InlineData(false, 0, "[suggest]", "setup", "--format")]
    [InlineData(true, 0, "[suggest]", "setup", "--format=json")]
    [InlineData(false, 0, "[suggest]", "setup", "--check=false", "--format")]
    [InlineData(false, 0, "--version", "prepare", "-f=json", "-f=plain")]
    public async Task ShouldSuppressAdvisories_SelectedParserAction_PreservesDispatch(bool expected, int expectedExit, params string[] args)
    {
        var root = TestParser.CreateRoot(new TestInteractionService());
        var setup = root.Subcommands.OfType<SetupCommand>().Single();
        setup.Aliases.Add("prepare");
        setup.FormatOption.Aliases.Add("-f");
        var parsed = root.Parse(args, new ParserConfiguration { EnablePosixBundling = false });
        var action = parsed.Action;
        var errors = parsed.Errors.ToArray();
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        using CancellationTokenSource cancellation = new();

        bool suppress = SetupCommand.ShouldSuppressAdvisories(parsed);
        parsed.Action.Should().BeSameAs(action);
        parsed.Errors.Should().Equal(errors);
        int exit = await parsed.InvokeAsync(new InvocationConfiguration
        {
            EnableDefaultExceptionHandler = false,
            Output = stdout,
            Error = stderr,
        }, cancellation.Token);

        suppress.Should().Be(expected);
        exit.Should().Be(expectedExit);
        parsed.Action.Should().BeSameAs(action);
        parsed.Errors.Should().Equal(errors);
        if (expectedExit == 0) stderr.ToString().Should().BeEmpty();
        else stderr.ToString().Should().Contain("Required argument missing for option: '--format'");
    }

    [Fact]
    public void SetupCommand_AutomationHelp_DescribesDefaultsAndCheckSideEffects()
    {
        var root = TestParser.CreateRoot(new TestInteractionService());
        var setup = root.Subcommands.OfType<SetupCommand>().Single();

        setup.YesOption.Description.Should().Contain("Skip the stack picker").And.Contain("project/runtime defaults");
        setup.CheckOption.Description.Should().Contain("without installing or updating workloads")
            .And.Contain("Metadata caches may change");
    }
}