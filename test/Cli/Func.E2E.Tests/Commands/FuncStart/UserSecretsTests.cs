// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Commands.FuncStart.Core;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncStart
{
    public class UserSecretsTests(ITestOutputHelper log) : BaseUserSecretsTests(log)
    {
        [Fact]
        public async Task Start_Dotnet_Isolated_WithUserSecrets_SuccessfulFunctionExecution()
        {
            await RunUserSecretsTest("dotnet-isolated", nameof(Start_Dotnet_Isolated_WithUserSecrets_SuccessfulFunctionExecution));
        }

        [Fact(Skip = "Test is not working as expected for dotnet-isolated. Need to further investigate why.")]
        public async Task Start_Dotnet_Isolated_WithUserSecrets_MissingStorageConnString_FailsWithExpectedError()
        {
            await RunMissingStorageConnString_FailsWithExpectedError("dotnet-isolated", nameof(Start_Dotnet_Isolated_WithUserSecrets_MissingStorageConnString_FailsWithExpectedError));
        }

        [Fact(Skip = "Test is not working as expected for dotnet-isolated. Need to further investigate why.")]
        public async Task Start_Dotnet_Isolated_WithUserSecrets_MissingBindingSetting_FailsWithExpectedError()
        {
            await RunWithUserSecrets_MissingBindingSetting_FailsWithExpectedError("dotnet-isolated", nameof(Start_Dotnet_Isolated_WithUserSecrets_MissingBindingSetting_FailsWithExpectedError));
        }
    }
}
