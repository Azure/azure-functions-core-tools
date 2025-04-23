// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Commands.FuncStart.Core;
using Azure.Functions.Cli.E2E.Tests.Traits;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncStart.InProcTests
{
    [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
    public class MissingConfigTests(ITestOutputHelper log) : BaseMissingConfigTests(log)
    {
        [Fact]
        public async Task Start_InProc_InvalidHostJson_FailsWithExpectedError()
        {
            await RunInvalidHostJsonTest("dotnet", nameof(Start_InProc_InvalidHostJson_FailsWithExpectedError));
        }

        [Fact]
        public async Task Start_InProc_MissingHostJson_FailsWithExpectedError()
        {
            await RunMissingHostJsonTest("dotnet", nameof(Start_InProc_MissingHostJson_FailsWithExpectedError));
        }

        [Theory]
        [InlineData("dotnet", "--worker-runtime None", "Use the up/down arrow keys to select a worker runtime:", false, false, false)] // Runtime parameter set to None, worker runtime prompt displayed
        [InlineData("dotnet", "", $"Use the up/down arrow keys to select a worker runtime:", false, false, false)] // Runtime parameter not provided, worker runtime prompt displayed
        public async Task Start_InProc_MissingLocalSettingsJson_BehavesAsExpected(string language, string runtimeParameter, string expectedOutput, bool invokeFunction, bool setRuntimeViaEnvironment, bool shouldWaitForHost)
        {
            await RunMissingLocalSettingsJsonTest(language, runtimeParameter, expectedOutput, invokeFunction, setRuntimeViaEnvironment, nameof(Start_InProc_MissingLocalSettingsJson_BehavesAsExpected), shouldWaitForHost);
        }
    }
}
