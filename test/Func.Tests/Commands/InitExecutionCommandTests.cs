// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands;

public sealed class InitExecutionCommandTests
{
    private readonly IInitExecutionRunner _runner = Substitute.For<IInitExecutionRunner>();
    private readonly TestInteractionService _interaction = new();
    private readonly TemplateConstraintEvaluationSnapshot _availability = new([]);

    [Fact]
    public async Task Invoke_TargetAndStaticOptions_BindsRequestWithoutLosingTemplateTail()
    {
        var command = CreateCommand();
        InitExecutionRequest? captured = null;
        _runner.ExecuteAsync(Arg.Do<InitExecutionRequest>(request => captured = request), _availability, Arg.Any<CancellationToken>())
            .Returns(new InitExecutionResult("control", true));
        string target = Path.Combine(Path.GetTempPath(), "init-command-target");

        int exit = await command.ParseInvocation([target, "-t", "basic", "-s", "node", "-l", "js", "-n", "Named",
            "--force", "--non-interactive", "--dry-run", "--output", "json", "--greeting=hello", "--param:Auth", "anonymous", "--verbose"]).InvokeAsync();

        exit.Should().Be(0);
        captured!.WorkingDirectory.Info.FullName.Should().Be(target);
        captured.WorkingDirectory.WasExplicit.Should().BeTrue();
        captured.TemplateReference.Should().Be("basic");
        captured.ExplicitStack.Should().Be("node");
        captured.ExplicitLanguage.Should().Be("js");
        captured.Name.Should().Be("Named");
        captured.Force.Should().BeTrue();
        captured.NonInteractive.Should().BeTrue();
        captured.DryRun.Should().BeTrue();
        captured.JsonOutput.Should().BeTrue();
        captured.RawTemplateTokens.Should().Equal("--greeting=hello", "--param:Auth", "anonymous");
        captured.ReservedAliases.Should().Contain(["--template", "-t", "--stack", "-s", "--language", "-l", "--name", "-n",
            "--output", "--force", "--dry-run", "--non-interactive", "--verbose", "--help", "-h"]);
        command.Options.Should().HaveCount(8);
    }

    [Theory]
    [InlineData("--greeting", "hello")]
    [InlineData("--greeting=hello", "--param:Auth=anonymous")]
    [InlineData("--", "extra")]
    [InlineData("@response-file")]
    public async Task Invoke_OmittedTarget_PreservesWholeRawTail(params string[] raw)
    {
        InitExecutionRequest? captured = null;
        _runner.ExecuteAsync(Arg.Do<InitExecutionRequest>(request => captured = request), _availability, Arg.Any<CancellationToken>())
            .Returns(InitExecutionResult.Reject("control"));

        await CreateCommand().ParseInvocation(["--template", "project", .. raw]).InvokeAsync();

        captured!.WorkingDirectory.WasExplicit.Should().BeFalse();
        captured.WorkingDirectory.Info.FullName.Should().Be(Directory.GetCurrentDirectory());
        captured.RawTemplateTokens.Should().Equal(raw);
    }

    [Fact]
    public async Task Invoke_PositionalValue_IsOnlyTargetNeverTemplate()
    {
        InitExecutionRequest? captured = null;
        _runner.ExecuteAsync(Arg.Do<InitExecutionRequest>(request => captured = request), _availability, Arg.Any<CancellationToken>())
            .Returns(InitExecutionResult.Reject("control"));

        await CreateCommand().ParseInvocation(["basic", "--dry-run"]).InvokeAsync();

        captured!.TemplateReference.Should().BeNull();
        captured.WorkingDirectory.Info.FullName.Should().Be(Path.Combine(Directory.GetCurrentDirectory(), "basic"));
        captured.RawTemplateTokens.Should().BeEmpty();
    }

    [Theory]
    [InlineData("--template")]
    [InlineData("--name")]
    [InlineData("--stack")]
    [InlineData("--language")]
    [InlineData("--output")]
    public async Task Invoke_MissingStaticValue_StopsBeforeRunner(string option)
    {
        var parsed = CreateCommand().ParseInvocation([option]);

        int exit = await parsed.InvokeAsync();

        exit.Should().NotBe(0);
        parsed.Errors.Should().NotBeEmpty();
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("\0")]
    public async Task Invoke_InvalidTarget_ReportsFailureWithoutRunner(string target)
    {
        int exit = await CreateCommand().ParseInvocation([target]).InvokeAsync();

        exit.Should().Be(1);
        _runner.ReceivedCalls().Should().BeEmpty();
        _interaction.AllOutput.Should().Contain("target directory");
    }

    [Fact]
    public async Task Invoke_HelpAndInvalidOutput_DoNotRunOrMutateGraph()
    {
        var command = CreateCommand();
        var before = command.Options.ToArray();

        await command.ParseInvocation(["--help"]).InvokeAsync();
        int exit = await command.ParseInvocation(["--output", "yaml"]).InvokeAsync();

        exit.Should().Be(1);
        _runner.ReceivedCalls().Should().BeEmpty();
        command.Options.Should().Equal(before);
        _interaction.AllOutput.Should().Contain("--output plain");
    }

    [Fact]
    public async Task Invoke_ConsecutiveInvocations_HaveFreshPathsAndCanonicalTails()
    {
        List<InitExecutionRequest> requests = [];
        _runner.ExecuteAsync(Arg.Do<InitExecutionRequest>(requests.Add), _availability, Arg.Any<CancellationToken>())
            .Returns(InitExecutionResult.Reject("control"));
        var command = CreateCommand();

        await command.ParseInvocation(["first", "--template=first-template", "--value=first"]).InvokeAsync();
        await command.ParseInvocation(["second", "--template=second-template", "--value", "second"]).InvokeAsync();

        requests.Select(request => request.TemplateReference).Should().Equal("first-template", "second-template");
        requests.Select(request => request.WorkingDirectory.Info.Name).Should().Equal("first", "second");
        requests[0].RawTemplateTokens.Should().Equal("--value=first");
        requests[1].RawTemplateTokens.Should().Equal("--value", "second");
    }

    [Theory]
    [InlineData("created", "Success", true)]
    [InlineData("partialInitialization", "ConfigurationFailure", true)]
    [InlineData("previewed", "Success", false)]
    [InlineData("invocationFailed", "FileConflict", false)]
    [InlineData("invocationFailed", "Failed", false)]
    [InlineData("invocationFailed", "EngineFailure", false)]
    [InlineData("created", "FileConflict", false)]
    [InlineData("created", "ConfigurationFailure", false)]
    [InlineData("partialInitialization", "Success", false)]
    [InlineData("created", "2147483647", false)]
    [InlineData("adopted", "Success", false)]
    [InlineData("repaired", "Success", false)]
    [InlineData("healed", "Success", false)]
    public async Task Invoke_ResultReturnsAfterCancellation_OnlyFreshConfirmedCreationReachesRenderer(
        string kind, string statusName, bool preserveResult)
    {
        using CancellationTokenSource cancellation = new();
        var status = Enum.Parse<TemplateInvocationStatus>(statusName);
        InitExecutionResult result = new(kind, kind == "created")
        {
            Invocation = new(status, []) { HasCommittedOutput = preserveResult },
        };
        _runner.ExecuteAsync(Arg.Any<InitExecutionRequest>(), _availability, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cancellation.Cancel();
            return result;
        });
        int? exit = null;

        var failure = await Record.ExceptionAsync(async () => exit = await CreateCommand().ParseInvocation(["--output", "json"])
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellation.Token));

        if (preserveResult)
        {
            failure.Should().BeNull();
            exit.Should().Be(result.Succeeded ? 0 : 1);
            _interaction.AllOutput.Should().Contain("JSON:");
        }
        else
        {
            failure.Should().BeAssignableTo<OperationCanceledException>();
            exit.Should().BeNull();
            _interaction.AllOutput.Should().NotContain("JSON:").And.NotContain("SUCCESS:");
        }
    }

    [Theory]
    [InlineData("created", "Success")]
    [InlineData("partialInitialization", "ConfigurationFailure")]
    public async Task Invoke_DryRunResultReturnsAfterCancellation_DoesNotTreatCreationShapeAsCommitted(string kind, string statusName)
    {
        using CancellationTokenSource cancellation = new();
        _runner.ExecuteAsync(Arg.Any<InitExecutionRequest>(), _availability, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cancellation.Cancel();
            return new InitExecutionResult(kind, kind == "created")
            {
                Invocation = new(Enum.Parse<TemplateInvocationStatus>(statusName), []) { HasCommittedOutput = true },
            };
        });

        Func<Task> act = () => CreateCommand().ParseInvocation(["--dry-run", "--output", "json"])
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _interaction.AllOutput.Should().NotContain("JSON:").And.NotContain("SUCCESS:");
    }

    [Fact]
    public async Task Invoke_RunnerTaskCanceled_DoesNotInventCompletedResult()
    {
        using CancellationTokenSource cancellation = new();
        _runner.ExecuteAsync(Arg.Any<InitExecutionRequest>(), _availability, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<InitExecutionResult>(cancellation.Token);
        });

        Func<Task> act = () => CreateCommand().ParseInvocation(["--output", "json"])
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _interaction.AllOutput.Should().NotContain("JSON:").And.NotContain("SUCCESS:");
    }

    [Theory]
    [InlineData("created", "Success", false, false)]
    [InlineData("partialInitialization", "ConfigurationFailure", false, false)]
    [InlineData("invocationFailed", "Failed", true, true)]
    [InlineData("invocationFailed", "EngineFailure", true, true)]
    [InlineData("previewed", "Success", true, false)]
    [InlineData("created", "Failed", true, false)]
    public async Task Invoke_ActualCommitMarkerAndResultKind_ControlLateCancellation(
        string kind, string statusName, bool committed, bool preserve)
    {
        using CancellationTokenSource cancellation = new();
        var result = new InitExecutionResult(kind, kind == "created")
        {
            Invocation = new(Enum.Parse<TemplateInvocationStatus>(statusName), []) { HasCommittedOutput = committed },
        };
        _runner.ExecuteAsync(Arg.Any<InitExecutionRequest>(), _availability, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cancellation.Cancel();
            return result;
        });
        int? exit = null;

        var failure = await Record.ExceptionAsync(async () => exit = await CreateCommand().ParseInvocation(["--output", "json"])
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellation.Token));

        if (preserve)
        {
            failure.Should().BeNull();
            exit.Should().Be(1);
            _interaction.Lines.Count(line => line.StartsWith("JSON: ", StringComparison.Ordinal)).Should().Be(1);
        }
        else
        {
            failure.Should().BeAssignableTo<OperationCanceledException>();
            exit.Should().BeNull();
            _interaction.AllOutput.Should().NotContain("JSON:");
        }

        _interaction.AllOutput.Should().NotContain("SUCCESS:");
    }

    [Fact]
    public void HasConfirmedCreationResult_NullResult_RejectsContractViolation()
    {
        Action act = () => InitExecutionRunner.HasConfirmedCreationResult(null!, dryRun: false);

        act.Should().Throw<ArgumentNullException>().WithParameterName("result");
    }

    private InitExecutionCommand CreateCommand() => new(_runner, new(_interaction), _availability);
}
