// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Commands.FuncStart.Core;
using Azure.Functions.Cli.E2E.Tests.Traits;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncStart
{
    public class UserSecretsTests(ITestOutputHelper log) : BaseUserSecretsTests(log)
    {
        [Fact]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.DotnetIsolated)]
        public async Task Start_Dotnet_Isolated_WithUserSecrets_SuccessfulFunctionExecution()
        {
            await RunUserSecretsTest("dotnet-isolated", nameof(Start_Dotnet_Isolated_WithUserSecrets_SuccessfulFunctionExecution));
        }

        [Fact]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.DotnetIsolated)]
        public async Task Start_Dotnet_Isolated_WithUserSecrets_MissingStorageConnString_FailsWithExpectedError()
        {
            await RunMissingStorageConnString("dotnet-isolated", false, nameof(Start_Dotnet_Isolated_WithUserSecrets_MissingStorageConnString_FailsWithExpectedError));
        }

        [Fact]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.DotnetIsolated)]
        public async Task Start_Dotnet_Isolated_WithUserSecrets_MissingBindingSetting_FailsWithExpectedError()
        {
            await RunWithUserSecrets_MissingBindingSettings("dotnet-isolated", nameof(Start_Dotnet_Isolated_WithUserSecrets_MissingBindingSetting_FailsWithExpectedError));
        }
    }
}
