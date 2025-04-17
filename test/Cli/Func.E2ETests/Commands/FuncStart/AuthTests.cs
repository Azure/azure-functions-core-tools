﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using FluentAssertions;
using Func.TestFramework.Assertions;
using Func.TestFramework.Commands;
using Func.TestFramework.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Func.E2ETests.Commands.FuncStart
{
    public class AuthTests : BaseE2ETests
    {
        public AuthTests(ITestOutputHelper log)
            : base(log)
        {
        }

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

            string methodName = "Start_DotnetIsolated_Test_EnableAuthFeature";
            string uniqueTestName = $"{methodName}_{authLevel}_{enableAuth}";

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
            {
                commandArgs.Add("--enableAuth");
            }

            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(commandArgs.ToArray());

            // Validate expected output content
            if (string.IsNullOrEmpty(expectedResult))
            {
                result.Should().HaveStdOutContaining("\"status\": \"401\"");
            }
            else
            {
                result.Should().HaveStdOutContaining("Selected out-of-process host.");
            }
        }
    }
}
