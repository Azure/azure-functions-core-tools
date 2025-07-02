// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

// Based off of: https://github.com/dotnet/sdk/blob/e793aa4709d28cd783712df40413448250e26fea/test/Microsoft.NET.TestFramework/Assertions/CommandResultAssertions.cs
using System.Text.RegularExpressions;
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

        public AndConstraint<CommandResultAssertions> StartOutOfProcessHost()
        {
            Execute.Assertion.ForCondition(_commandResult.StdOut is not null && _commandResult.StdOut.Contains("4.10") && _commandResult.StdOut.Contains("Selected out-of-process host."))
                .FailWith($"The command output did not contain expected result for out of process host.{Environment.NewLine}");
            return new AndConstraint<CommandResultAssertions>(this);
        }

        public AndConstraint<CommandResultAssertions> StartDefaultHost()
        {
            Execute.Assertion.ForCondition(_commandResult.StdOut is not null && _commandResult.StdOut.Contains("Selected default host."))
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

        public AndConstraint<CommandResultAssertions> WriteDockerfile()
        {
            const string pattern = $"Writing Dockerfile";
            Execute.Assertion.ForCondition(_commandResult.StdOut is not null && _commandResult.StdOut.Contains(pattern))
                .FailWith($"The command output did not contain expected result: {pattern}{Environment.NewLine}");
            return new AndConstraint<CommandResultAssertions>(this);
        }

        public AndConstraint<CommandResultAssertions> WriteVsCodeExtensionsJsonAndExitWithZero(string workingDirectory)
        {
            var vsCodeExtPattern = @"Writing.*[\\/]\.vscode[\\/]*extensions\.json";
            var gitInitPattern = "Initialized empty Git repository";

            Execute.Assertion.ForCondition(_commandResult.ExitCode == 0)
                .FailWith($"Expected command to exit with 0 but it did not. Error message: {_commandResult.StdErr}");

            Execute.Assertion.ForCondition(_commandResult.StdOut is not null && Regex.IsMatch(_commandResult.StdOut, vsCodeExtPattern))
                .FailWith($"The command output did not contain expected (using regex pattern): {vsCodeExtPattern}{Environment.NewLine}");

            Execute.Assertion.ForCondition(_commandResult.StdOut is not null && !_commandResult.StdOut.Contains(gitInitPattern))
                .FailWith($"The command output did contain unexpected result: {gitInitPattern}{Environment.NewLine}");

            return new AndConstraint<CommandResultAssertions>(this);
        }

        public AndConstraint<CommandResultAssertions> FilesExistsWithExpectContent(List<(string FilePath, string[] ExpectedContents)> filesWithExpectedContents)
        {
            foreach (var file in filesWithExpectedContents)
            {
                Execute.Assertion.ForCondition(File.Exists(file.FilePath))
                    .FailWith($"File '{file.FilePath}' to exist, but it does not.");

                var actualContent = File.ReadAllText(file.FilePath);
                Execute.Assertion.ForCondition(!string.IsNullOrEmpty(actualContent))
                    .FailWith($"File '{file.FilePath}' to have content, but it was empty.");

                foreach (var expectedContent in file.ExpectedContents)
                {
                    Execute.Assertion.ForCondition(actualContent.Contains(expectedContent))
                        .FailWith($"File '{file.FilePath}' should contain '{expectedContent}', but it did not.");
                }
            }

            return new AndConstraint<CommandResultAssertions>(this);
        }

        public AndConstraint<CommandResultAssertions> FileDoesNotContain(string filePath, params string[] unexpectedContents)
        {
            Execute.Assertion.ForCondition(File.Exists(filePath))
                .FailWith($"File '{filePath}' does not exist.");

            var actualContent = File.ReadAllText(filePath);

            foreach (var input in unexpectedContents)
            {
                Execute.Assertion.ForCondition(!actualContent.Contains(input))
                    .FailWith($"File '{filePath}' should not contain '{input}', but it does.");
            }

            return new AndConstraint<CommandResultAssertions>(this);
        }

        public AndConstraint<CommandResultAssertions> HaveStdOutMatchesRegex(string pattern)
        {
            Execute.Assertion.ForCondition(
                    _commandResult.StdOut is not null &&
                    System.Text.RegularExpressions.Regex.IsMatch(_commandResult.StdOut, pattern))
                .FailWith($"The command output did not match the regex pattern: {pattern}{Environment.NewLine}");
            return new AndConstraint<CommandResultAssertions>(this);
        }
    }
}
