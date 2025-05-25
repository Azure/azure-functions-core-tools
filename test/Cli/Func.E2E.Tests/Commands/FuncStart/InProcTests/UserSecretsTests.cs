// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Commands.FuncStart.Core;
using Azure.Functions.Cli.E2E.Tests.Traits;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncStart.InProcTests
{
    [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Dotnet)]
    public class UserSecretsTests(ITestOutputHelper log) : BaseUserSecretsTests(log)
    {
        [Fact]
        public async Task Start_InProc_WithUserSecrets_SuccessfulFunctionExecution()
        {
            await RunUserSecretsTest("dotnet", nameof(Start_InProc_WithUserSecrets_SuccessfulFunctionExecution));
        }

        [Fact]
        public async Task Start_InProc_WithUserSecrets_MissingStorageConnString_FailsWithExpectedError()
        {
            await RunMissingStorageConnString_FailsWithExpectedError("dotnet", nameof(Start_InProc_WithUserSecrets_MissingStorageConnString_FailsWithExpectedError));
        }

        [Fact]
        public async Task Start_InProc_WithUserSecrets_MissingBindingSetting_FailsWithExpectedError()
        {
            await RunWithUserSecrets_MissingBindingSetting_FailsWithExpectedError("dotnet", nameof(Start_InProc_WithUserSecrets_MissingBindingSetting_FailsWithExpectedError));
        }
    }
}
