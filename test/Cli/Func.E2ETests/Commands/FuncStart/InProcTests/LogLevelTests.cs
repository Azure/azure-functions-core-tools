// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Commands.FuncStart.Core;
using Azure.Functions.Cli.E2ETests.Traits;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncStart.InProcTests
{
    [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Dotnet)]
    public class LogLevelTests(ITestOutputHelper log) : BaseLogLevelTests(log)
    {
        [Fact]
        public async Task Start_InProc_LogLevelOverridenViaHostJson_LogLevelSetToExpectedValue()
        {
            await RunLogLevelOverridenViaHostJsonTest("dotnet-isolated", nameof(Start_InProc_LogLevelOverridenViaHostJson_LogLevelSetToExpectedValue));
        }

        [Fact]
        public async Task Start_InProc_LogLevelOverridenWithFilter_LogLevelSetToExpectedValue()
        {
            await RunLogLevelOverridenWithFilterTest("dotnet-isolated", nameof(Start_InProc_LogLevelOverridenWithFilter_LogLevelSetToExpectedValue));
        }
    }
}
