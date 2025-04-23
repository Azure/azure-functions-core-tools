// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

// Based off of: https://github.com/dotnet/sdk/blob/e793aa4709d28cd783712df40413448250e26fea/test/Microsoft.NET.TestFramework/Assertions/CommandResultAssertions.cs
using Azure.Functions.Cli.Abstractions;
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

        public AndConstraint<CommandResultAssertions> StartInProc6Host()
        {
            Execute.Assertion.ForCondition(_commandResult.StdOut is not null && _commandResult.StdOut.Contains("Starting child process for inproc6 model host.") && _commandResult.StdOut.Contains("Selected inproc6 host."))
                .FailWith($"The command output did not contain expected result for inproc6 host.{Environment.NewLine}");
            return new AndConstraint<CommandResultAssertions>(this);
        }

        public AndConstraint<CommandResultAssertions> StartInProc8Host()
        {
            Execute.Assertion.ForCondition(_commandResult.StdOut is not null && _commandResult.StdOut.Contains("Starting child process for inproc8 model host.") && _commandResult.StdOut.Contains("Selected inproc8 host."))
                .FailWith($"The command output did not contain expected result for inproc8 host.{Environment.NewLine}");
            return new AndConstraint<CommandResultAssertions>(this);
        }

        public AndConstraint<CommandResultAssertions> StartDefaultHost()
        {
            Execute.Assertion.ForCondition(_commandResult.StdOut is not null && _commandResult.StdOut.Contains("4.10") && _commandResult.StdOut.Contains("Selected out-of-process host."))
                .FailWith($"The command output did not contain expected result for default host.{Environment.NewLine}");
            return new AndConstraint<CommandResultAssertions>(this);
        }

        public AndConstraint<CommandResultAssertions> LoadNet6HostVisualStudio()
        {
            Execute.Assertion.ForCondition(_commandResult.StdOut is not null && _commandResult.StdOut.Contains("Loading .NET 6 host"))
                .FailWith($"The command output did not contain expected result for .NET 6 host.{Environment.NewLine}");
            return new AndConstraint<CommandResultAssertions>(this);
        }

        public AndConstraint<CommandResultAssertions> LoadNet8HostVisualStudio()
        {
            Execute.Assertion.ForCondition(_commandResult.StdOut is not null && _commandResult.StdOut.Contains("Loading .NET 8 host"))
                .FailWith($"The command output did not contain expected result for .NET 8 host.{Environment.NewLine}");
            return new AndConstraint<CommandResultAssertions>(this);
        }
    }
}
