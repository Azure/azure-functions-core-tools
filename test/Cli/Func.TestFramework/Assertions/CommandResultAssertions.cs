using FluentAssertions;
using FluentAssertions.Execution;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Func.TestFramework.Assertions
{
    public class CommandResultAssertions
    {
        private CommandResult _commandResult;

        public CommandResultAssertions(CommandResult commandResult)
        {
            _commandResult = commandResult;
        }

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
