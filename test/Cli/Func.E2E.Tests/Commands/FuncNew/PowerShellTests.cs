// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncNew
{
    public class PowerShellTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        [Trait(TestTraits.WorkerRuntime, WorkerRuntimeTraits.Powershell)]
        public async void FuncNew_HttpTrigger_AuthLevelConfigured_PowerShell_Succeeds()
        {
            var testName = nameof(FuncNew_HttpTrigger_AuthLevelConfigured_PowerShell_Succeeds);
            var functionJsonPath = Path.Combine(WorkingDirectory, "MyHttpTriggerFunction", Common.Constants.FunctionJsonFileName);
            var expectedcontent = new[] { "\"authLevel\": \"anonymous\"", "\"type\": \"httpTrigger\"" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (functionJsonPath, expectedcontent)
            };

            // Initialize PowerShell project
            await FuncInitWithRetryAsync(testName, new[] { ".", "--worker-runtime", "powershell" });

            // Run func new
            var args = new[] { ".", "--template", "HttpTrigger", "--name", "MyHttpTriggerFunction", "--authlevel", "Anonymous", "-a" };
            var result = await FuncNewWithResultRetryAsync(testName, args, "powershell");

            // Verify output contains success message
            result.Should().HaveStdOutContaining("The function \"MyHttpTriggerFunction\" was created successfully from the \"HttpTrigger\" template.");
            result.Should().FilesExistsWithExpectContent(filesToValidate);
        }
    }
}
