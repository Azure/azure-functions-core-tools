using SampleTestingUpdate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Compression;
using FluentAssertions.Execution;


namespace TestingFrameworkAgain
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
                .FailWith($"Expected command to exit with {expectedExitCode} but it did not.");
            return this;
        }

        public CommandResultAssertions Pass()
        {
            Execute.Assertion.ForCondition(_commandResult.ExitCode == 0)
                .FailWith($"Expected command to pass but it did not.");
            return this;
        }

        public CommandResultAssertions Fail()
        {
            Execute.Assertion.ForCondition(_commandResult.ExitCode != 0)
                .FailWith($"Expected command to fail but it did not.");
            return this;
        }
    }
}
