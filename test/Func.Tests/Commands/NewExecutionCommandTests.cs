// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands;

public sealed class NewExecutionCommandTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly INewExecutionRunner _runner = Substitute.For<INewExecutionRunner>();
    private readonly TemplateConstraintEvaluationSnapshot _availability = new([]);

    [Theory]
    [InlineData("timer", null)]
    [InlineData("--template", "timer")]
    [InlineData("-t", "timer")]
    public async Task Invoke_SelectorAndStableOptions_BindsImmutableRequest(string first, string? second)
    {
        var (root, command) = CreateCommand();
        NewExecutionRequest? captured = null;
        _runner.ExecuteAsync(Arg.Do<NewExecutionRequest>(request => captured = request), _availability, Arg.Any<CancellationToken>())
            .Returns(new NewExecutionResult("previewed", true));
        string path = Path.Combine(Path.GetTempPath(), "func-command-binding");
        string[] selector = second is null ? [first] : [first, second];
        var parsed = Parse(root, [.. selector, "--path", path, "-n", "TimerName", "--language", "ts",
            "--output", "json", "--force", "--non-interactive", "--dry-run", "--greeting=Hello", "--param:Auth=anonymous"]);

        int exit = await parsed.InvokeAsync();

        exit.Should().Be(0);
        captured.Should().NotBeNull();
        captured!.TemplateReference.Should().Be("timer");
        captured.WorkingDirectory.Info.FullName.Should().Be(path);
        captured.Name.Should().Be("TimerName");
        captured.ExplicitLanguage.Should().Be("ts");
        captured.Force.Should().BeTrue();
        captured.NonInteractive.Should().BeTrue();
        captured.DryRun.Should().BeTrue();
        captured.JsonOutput.Should().BeTrue();
        captured.RawTemplateTokens.Should().Equal("--greeting=Hello", "--param:Auth=anonymous");
        captured.ReservedAliases.Should().Contain(["--name", "-n", "--path", "--language", "--verbose", "--help", "-h", "install"]);
        command.Options.Should().HaveCount(9);
    }

    [Theory]
    [InlineData("--foo", "value")]
    [InlineData("--foo=bar", "--param:Auth=anonymous")]
    [InlineData("--", "tail")]
    [InlineData("@not-a-response-file")]
    public async Task Invoke_UnknownTokensOccupyPositionalSlot_PreservesWholeTail(params string[] raw)
    {
        var (root, _) = CreateCommand();
        NewExecutionRequest? captured = null;
        _runner.ExecuteAsync(Arg.Do<NewExecutionRequest>(request => captured = request), _availability, Arg.Any<CancellationToken>())
            .Returns(NewExecutionResult.Reject("control", "Not executed."));

        await Parse(root, raw).InvokeAsync();

        captured.Should().NotBeNull();
        if (raw[0].StartsWith('-'))
        {
            captured!.TemplateReference.Should().BeNull();
            captured.RawTemplateTokens.Should().Equal(raw);
        }
        else
        {
            captured!.TemplateReference.Should().Be(raw[0]);
        }
    }

    [Theory]
    [InlineData("timer", "--template", "timer")]
    [InlineData("timer", "-t", "different")]
    public async Task Invoke_BothSelectors_StopsBeforeRunner(params string[] tokens)
    {
        var (root, _) = CreateCommand();

        var parsed = Parse(root, tokens);
        int exit = await parsed.InvokeAsync();

        exit.Should().NotBe(0);
        parsed.Errors.Should().Contain(error => error.Message.Contains("not both", StringComparison.Ordinal));
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("install")]
    [InlineData("update")]
    [InlineData("uninstall")]
    public async Task Invoke_RegisteredLifecycleName_RoutesChildUnlessExplicitSelector(string name)
    {
        var (_, command) = CreateCommand();
        int childCalls = 0;
        Func<Command> lifecycle = () =>
        {
            Command child = new(name);
            child.Options.Add(new Option<bool>("--lifecycle-option"));
            child.SetAction(_ => childCalls++);
            return child;
        };
        _runner.ExecuteAsync(Arg.Any<NewExecutionRequest>(), _availability, Arg.Any<CancellationToken>())
            .Returns(NewExecutionResult.Reject("control"));

        await command.ParseInvocation([name], [lifecycle]).InvokeAsync();
        await command.ParseInvocation(["--template", name], [lifecycle]).InvokeAsync();

        childCalls.Should().Be(1);
        await _runner.Received(1).ExecuteAsync(
            Arg.Is<NewExecutionRequest>(request => request.TemplateReference == name
                && request.ReservedAliases.Contains("--lifecycle-option")), _availability, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("install", true)]
    [InlineData("update", true)]
    [InlineData("uninstall", true)]
    [InlineData("install", false)]
    [InlineData("update", false)]
    [InlineData("uninstall", false)]
    public async Task Invoke_LifecycleNameAsSeparatedTemplateValue_DoesNotDispatchChild(string name, bool explicitSelector)
    {
        var (_, command) = CreateCommand();
        int childCalls = 0;
        Func<Command> lifecycle = () =>
        {
            Command child = new(name);
            child.SetAction(_ => childCalls++);
            return child;
        };
        NewExecutionRequest? captured = null;
        _runner.ExecuteAsync(Arg.Do<NewExecutionRequest>(request => captured = request), _availability, Arg.Any<CancellationToken>())
            .Returns(NewExecutionResult.Reject("control"));

        string[] selector = explicitSelector ? ["--template", "timer"] : ["timer"];
        await command.ParseInvocation([.. selector, "--greeting", name], [lifecycle]).InvokeAsync();

        childCalls.Should().Be(0);
        captured.Should().NotBeNull();
        captured!.RawTemplateTokens.Should().Equal("--greeting", name);
    }

    [Theory]
    [InlineData("--verbose")]
    [InlineData("--force")]
    [InlineData("--force=false")]
    [InlineData("--name=FunctionName")]
    public async Task Invoke_HostPrefixBeforeLifecycle_PreservesChildRouting(string prefix)
    {
        var (_, command) = CreateCommand();
        int childCalls = 0;
        Func<Command> lifecycle = () =>
        {
            Command child = new("install");
            child.SetAction(_ => childCalls++);
            return child;
        };

        await command.ParseInvocation([prefix, "install"], [lifecycle]).InvokeAsync();

        childCalls.Should().Be(1);
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("--name")]
    [InlineData("--path")]
    public async Task Invoke_LifecycleNameAsHostValue_IsNotALifecycleSelector(string option)
    {
        var (_, command) = CreateCommand();
        int childCalls = 0;
        Func<Command> lifecycle = () =>
        {
            Command child = new("install");
            child.SetAction(_ => childCalls++);
            return child;
        };
        _runner.ExecuteAsync(Arg.Any<NewExecutionRequest>(), _availability, Arg.Any<CancellationToken>())
            .Returns(NewExecutionResult.Reject("control"));

        await command.ParseInvocation([option, "install"], [lifecycle]).InvokeAsync();

        childCalls.Should().Be(0);
        await _runner.Received(1).ExecuteAsync(Arg.Any<NewExecutionRequest>(), _availability, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_Help_DoesNotResolveOrMutateCommand()
    {
        var (root, command) = CreateCommand();
        var before = command.Options.ToArray();

        await Parse(root, ["--help"]).InvokeAsync();

        command.Options.Should().Equal(before);
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("--path")]
    [InlineData("--template")]
    [InlineData("--name")]
    [InlineData("--language")]
    public async Task Invoke_MissingHostValue_IsParserFailure(string option)
    {
        var (root, _) = CreateCommand();

        var parsed = Parse(root, ["timer", option]);
        int exit = await parsed.InvokeAsync();

        exit.Should().NotBe(0);
        parsed.Errors.Should().NotBeEmpty();
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Invoke_ConsecutiveContextsAndInlineHostValues_NoStaleOptionsOrLostTail()
    {
        var (root, command) = CreateCommand();
        List<NewExecutionRequest> captured = [];
        _runner.ExecuteAsync(Arg.Do<NewExecutionRequest>(captured.Add), _availability, Arg.Any<CancellationToken>())
            .Returns(NewExecutionResult.Reject("control"));
        var before = command.Options.ToArray();

        await Parse(root, ["timer", "--name=First", "--greeting=one", "--verbose"]).InvokeAsync();
        await Parse(root, ["--template", "http", "--greeting", "two", "--name=Second"]).InvokeAsync();

        captured.Select(request => request.Name).Should().Equal("First", "Second");
        captured[0].RawTemplateTokens.Should().Equal("--greeting=one");
        captured[1].RawTemplateTokens.Should().Equal("--greeting", "two");
        command.Options.Should().Equal(before);
    }

    [Fact]
    public async Task Invoke_DefaultPath_UsesDomainCwdWithoutCreatingDirectory()
    {
        var (root, _) = CreateCommand();
        _runner.ExecuteAsync(Arg.Any<NewExecutionRequest>(), _availability, Arg.Any<CancellationToken>())
            .Returns(NewExecutionResult.Reject("control"));

        await Parse(root, ["timer"]).InvokeAsync();

        await _runner.Received(1).ExecuteAsync(Arg.Is<NewExecutionRequest>(request =>
            !request.WorkingDirectory.WasExplicit && request.WorkingDirectory.Info.FullName == Directory.GetCurrentDirectory()),
            _availability, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_InvalidOutput_RejectsBeforeRunner()
    {
        var (root, _) = CreateCommand();

        int exit = await Parse(root, ["timer", "--output", "yaml"]).InvokeAsync();

        exit.Should().Be(1);
        _interaction.AllOutput.Should().Contain("--output plain");
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("\0")]
    public async Task Invoke_InvalidExplicitPath_IsParserFailure(string path)
    {
        var (root, _) = CreateCommand();

        var parsed = Parse(root, ["timer", "--path", path]);
        int exit = await parsed.InvokeAsync();

        exit.Should().NotBe(0);
        parsed.Errors.Should().Contain(error => error.Message.Contains("Invalid --path", StringComparison.Ordinal));
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Invoke_RelativePath_BindsThroughWorkingDirectoryDomain()
    {
        var (root, _) = CreateCommand();
        _runner.ExecuteAsync(Arg.Any<NewExecutionRequest>(), _availability, Arg.Any<CancellationToken>())
            .Returns(NewExecutionResult.Reject("control"));

        await Parse(root, ["timer", "--path", "nested-consumer-binding"]).InvokeAsync();

        await _runner.Received(1).ExecuteAsync(Arg.Is<NewExecutionRequest>(request =>
            request.WorkingDirectory.Info.FullName == Path.Combine(Directory.GetCurrentDirectory(), "nested-consumer-binding")
                && request.WorkingDirectory.WasExplicit), _availability, Arg.Any<CancellationToken>());
    }

    private (RootCommand Root, NewExecutionCommand Command) CreateCommand()
    {
        NewExecutionCommand command = new(_runner, new(_interaction), _availability);
        FuncRootCommand root = [command];
        return (root, command);
    }

    internal static ParseResult Parse(RootCommand root, string[] tokens)
        => root.Subcommands.OfType<NewExecutionCommand>().Single().ParseInvocation(tokens);
}
