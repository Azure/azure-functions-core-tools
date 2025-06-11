// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncVersion
{
    public class VersionTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Theory]
        [InlineData("-v")]
        [InlineData("-version")]
        [InlineData("--version")]
        public void Version_DisplaysVersionNumber(string args)
        {
            var testName = nameof(Version_DisplaysVersionNumber);
            var versionCommand = new FuncVersionCommand(FuncPath, testName, Log);

            // Execute the command
            var result = versionCommand
                        .WithWorkingDirectory(WorkingDirectory)
                        .Execute(new[] { args });

            // Verify the output contains a version number starting with "4."
            result.Should().HaveStdOutContaining("4.");
            result.Should().ExitWith(0);
        }
    }
}
