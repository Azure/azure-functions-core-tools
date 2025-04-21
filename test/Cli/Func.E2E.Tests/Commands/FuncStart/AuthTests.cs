// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncStart
{
    public class AuthTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Theory]
        [InlineData("function", false, "Welcome to Azure Functions!")]
        [InlineData("function", true, "")]
        [InlineData("anonymous", true, "Welcome to Azure Functions!")]
        public async Task Start_DotnetIsolated_Test_EnableAuthFeature(
            string authLevel,
            bool enableAuth,
            string expectedResult)
        {
            int port = ProcessHelper.GetAvailablePort();

            var methodName = "Start_DotnetIsolated_Test_EnableAuthFeature";
            var uniqueTestName = $"{methodName}_{authLevel}_{enableAuth}";

            // Call func init and func new
            await FuncInitWithRetryAsync(uniqueTestName, new[] { ".", "--worker-runtime", "dotnet-isolated" });
            await FuncNewWithRetryAsync(uniqueTestName, new[] { ".", "--template", "Httptrigger", "--name", "HttpTrigger", "--authlevel", authLevel });

            // Call func start
            var funcStartCommand = new FuncStartCommand(FuncPath, methodName, Log);
            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter, "HttpTrigger");
            };

            // Build command arguments based on enableAuth parameter
            var commandArgs = new List<string> { "start", "--verbose", "--port", port.ToString() };
            if (enableAuth)
                commandArgs.Add("--enableAuth");

            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(commandArgs.ToArray());

            // Validate expected output content
            if (string.IsNullOrEmpty(expectedResult))
                result.Should().HaveStdOutContaining("\"status\": \"401\"");
            else
                result.Should().HaveStdOutContaining("Selected out-of-process host.");
        }
    }
}
