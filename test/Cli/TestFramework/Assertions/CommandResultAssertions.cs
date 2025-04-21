// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

// Based off of: https://github.com/dotnet/sdk/blob/e793aa4709d28cd783712df40413448250e26fea/test/Microsoft.NET.TestFramework/Assertions/CommandResultAssertions.cs
using Azure.Functions.Cli.Abstractions.Command;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Azure.Functions.Cli.TestFramework.Assertions
{
    public class CommandResultAssertions(CommandResult commandResult)
    {
        private readonly CommandResult _commandResult = commandResult;

        public CommandResultAssertions ExitWith(int expectedExitCode)
        {
            Execute.Assertion.ForCondition(_commandResult.ExitCode == expectedExitCode)
                .FailWith($"Expected command to exit with {expectedExitCode} but it did not. Error message: {_commandResult.StdErr}");
            return this;
        }

        public AndConstraint<CommandResultAssertions> HaveStdOutContaining(string pattern)
        {
            Execute.Assertion.ForCondition(_commandResult.StdOut is not null && _commandResult.StdOut.Contains(pattern))
                .FailWith($"The command output did not contain expected result: {pattern}{Environment.NewLine}");
            return new AndConstraint<CommandResultAssertions>(this);
        }

        public AndConstraint<CommandResultAssertions> HaveStdErrContaining(string pattern)
        {
            Execute.Assertion.ForCondition(_commandResult.StdErr is not null && _commandResult.StdErr.Contains(pattern))
                .FailWith($"The command output did not contain expected result: {pattern}{Environment.NewLine}");
            return new AndConstraint<CommandResultAssertions>(this);
        }

        public AndConstraint<CommandResultAssertions> NotHaveStdOutContaining(string pattern)
        {
            Execute.Assertion.ForCondition(_commandResult.StdOut is not null && !_commandResult.StdOut.Contains(pattern))
                .FailWith($"The command output did contain expected result: {pattern}{Environment.NewLine}");
            return new AndConstraint<CommandResultAssertions>(this);
        }
    }
}
