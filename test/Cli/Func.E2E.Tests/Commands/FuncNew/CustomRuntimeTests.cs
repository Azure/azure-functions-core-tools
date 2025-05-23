// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncNew
{
    [Trait(TestTraits.WorkerRuntime, WorkerRuntimeTraits.Custom)]
    public class CustomRuntimeTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        public async Task FuncNew_HttpTrigger_CreatesFunctionSuccessfully_CustomRuntime()
        {
            var methodName = nameof(FuncNew_HttpTrigger_CreatesFunctionSuccessfully_CustomRuntime);
            var uniqueTestName = methodName;

            // Initialize function app
            await FuncInitWithRetryAsync(uniqueTestName, new[] { ".", "--worker-runtime", "custom", "--no-bundle" });

            // Run func new
            var funcNewCommand = new FuncNewCommand(FuncPath, uniqueTestName, Log);
            var result = funcNewCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { ".", "--template", "HttpTrigger", "--name", "testfunc" });

            // Validate result
            result.Should().HaveStdOutContaining("The function \"testfunc\" was created successfully from the \"HttpTrigger\" template.");
        }
    }
}
