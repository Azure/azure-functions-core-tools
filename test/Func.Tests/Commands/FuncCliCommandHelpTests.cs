// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using Azure.Functions.Cli.Commands;

namespace Azure.Functions.Cli.Tests.Commands;

public sealed class FuncCliCommandHelpTests
{
    [Theory]
    [InlineData("workload", "install")]
    [InlineData("profile", "set")]
    public async Task LegacyBareParent_PreservesSynchronousHelp(string command, string child)
    {
        TestInteractionService interaction = new();
        var root = TestParser.CreateRoot(interaction);

        int exit = await root.Parse([command]).InvokeAsync();

        exit.Should().Be(0);
        interaction.AllOutput.Should().Contain("func " + command).And.Contain(child);
    }

    [Fact]
    public async Task DefaultExecution_AwaitsAsyncHelpWithOriginalParseAndCancellation()
    {
        ParentCommand command = new();
        RootCommand root = [command];
        using CancellationTokenSource cancellation = new();
        ReviewAsyncHelp action = new();
        root.Options.OfType<HelpOption>().Single().Action = action;
        var parsed = root.Parse(["parent"]);

        var execute = (AsynchronousCommandLineAction)command.Action!;
        int exit = await execute.InvokeAsync(parsed, cancellation.Token);

        exit.Should().Be(17);
        action.Parsed.Should().BeSameAs(parsed);
        action.Token.Should().Be(cancellation.Token);
    }

    [Fact]
    public async Task DefaultExecution_AsyncHelpFailureIsNotSwallowed()
    {
        ParentCommand command = new();
        RootCommand root = [command];
        var original = new InvalidOperationException("controlled help failure");
        root.Options.OfType<HelpOption>().Single().Action = new ReviewAsyncHelp(original);
        var parsed = root.Parse(["parent"]);

        Func<Task> execute = () => parsed.InvokeAsync(new() { EnableDefaultExceptionHandler = false }, CancellationToken.None);

        (await execute.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(original);
    }

    private sealed class ParentCommand() : FuncCliCommand("parent", "Parent-only command.");

    private sealed class ReviewAsyncHelp(Exception? failure = null) : AsynchronousCommandLineAction
    {
        public ParseResult? Parsed { get; private set; }
        public CancellationToken Token { get; private set; }

        public override async Task<int> InvokeAsync(ParseResult parseResult, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            Parsed = parseResult;
            Token = cancellationToken;
            if (failure is not null)
            {
                throw failure;
            }

            return 17;
        }
    }
}
