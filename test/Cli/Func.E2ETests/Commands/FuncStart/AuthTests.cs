// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncStart
{
    public class AuthTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Theory]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.DotnetIsolated)]
        [InlineData("function", false, "Welcome to Azure Functions!")]
        [InlineData("function", true, "")]
        [InlineData("anonymous", true, "Welcome to Azure Functions!")]
        public async Task Start_DotnetIsolated_EnableAuthFeature(
            string authLevel,
            bool enableAuth,
            string expectedResult)
        {
            var port = ProcessHelper.GetAvailablePort();

            var methodName = nameof(Start_DotnetIsolated_EnableAuthFeature);
            var uniqueTestName = $"{methodName}_{authLevel}_{enableAuth}";

            // Call func init and func new
            await FuncInitWithRetryAsync(uniqueTestName, [".", "--worker-runtime", "dotnet-isolated"]);
            await FuncNewWithRetryAsync(uniqueTestName, [".", "--template", "Httptrigger", "--name", "HttpTrigger", "--authlevel", authLevel]);

            // Call func start
            var funcStartCommand = new FuncStartCommand(FuncPath, methodName, Log);
            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand)), "HttpTrigger");
            };

            // Build command arguments based on enableAuth parameter
            var commandArgs = new List<string> { "start", "--verbose", "--port", port.ToString() };
            if (enableAuth)
            {
                commandArgs.Add("--enableAuth");
            }

            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute([.. commandArgs]);

            // Validate expected output content
            if (string.IsNullOrEmpty(expectedResult))
            {
                result.Should().HaveStdOutContaining("\"status\": \"401\"");
            }
            else
            {
                result.Should().StartOutOfProcessHost();
            }
        }
    }
}
