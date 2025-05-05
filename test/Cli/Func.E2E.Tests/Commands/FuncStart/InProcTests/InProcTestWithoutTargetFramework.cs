// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncStart.InProcTests
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Dotnet)]
    public class InProcTestWithoutTargetFramework(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
        public async Task Start_InProc_SuccessfulFunctionExecution()
        {
            var port = ProcessHelper.GetAvailablePort();
            var testName = nameof(Start_InProc_SuccessfulFunctionExecution);

            // Initialize dotnet function app using retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "dotnet"]);

            // Add HTTP trigger using retry helper
            await FuncNewWithRetryAsync(testName, [".", "--template", "HttpTrigger", "--name", "HttpTrigger"]);

            // Call func start
            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            string? capturedContent = null;

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                capturedContent = await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "HttpTrigger?name=Test");
            };

            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--port", port.ToString()]);

            capturedContent.Should().Be("Hello, Test. This HTTP triggered function executed successfully.");
        }
    }
}
