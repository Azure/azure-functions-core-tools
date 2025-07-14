// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Commands.FuncStart.Core;
using Azure.Functions.Cli.E2ETests.Traits;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncStart.InProcTests
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Dotnet)]
    public class MissingConfigTests(ITestOutputHelper log) : BaseMissingConfigTests(log)
    {
        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
        public async Task Start_InProc_InvalidHostJson_FailsWithExpectedError()
        {
            await RunInvalidHostJsonTest("dotnet", true, nameof(Start_InProc_InvalidHostJson_FailsWithExpectedError));
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
        public async Task Start_InProc_MissingHostJson_FailsWithExpectedError()
        {
            await RunMissingHostJsonTest("dotnet", true, nameof(Start_InProc_MissingHostJson_FailsWithExpectedError));
        }

        [Theory]
        [InlineData("dotnet", "--worker-runtime None", false, false)] // Runtime parameter set to None, worker runtime prompt displayed
        [InlineData("dotnet", "", false, false)] // Runtime parameter not provided, worker runtime prompt displayed
        public async Task Start_InProc_MissingLocalSettingsJson_BehavesAsExpected(string language, string runtimeParameter, bool invokeFunction, bool setRuntimeViaEnvironment)
        {
            string expectedOutput;
            if (OperatingSystem.IsWindows())
            {
                expectedOutput = "Use the up/down arrow keys to select a worker runtime:";
            }
            else
            {
                expectedOutput = "Select a number for ";
            }

            await RunMissingLocalSettingsJsonTest(language, runtimeParameter, expectedOutput, invokeFunction, setRuntimeViaEnvironment, nameof(Start_InProc_MissingLocalSettingsJson_BehavesAsExpected));
        }
    }
}
