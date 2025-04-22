// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using FluentAssertions;
using Func.E2ETests.Traits;
using Xunit;
using Xunit.Abstractions;
using Azure.Functions.Cli.E2E.Tests.Commands.FuncStart.Core;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncStart.InProcTests
{
    [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
    public class UserSecretsTests(ITestOutputHelper log) : BaseUserSecretsTests (log)
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
